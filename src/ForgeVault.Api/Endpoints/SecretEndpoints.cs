using System.Security.Claims;
using System.Text;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md and docs/modules/05_ACCESS_BROKER_AND_API.md.
// Core vertical slice of milestone M4, with RBAC + audit added in M5
// (docs/architecture/IMPLEMENTATION_READINESS.md). No endpoint except the value-reveal one
// below ever returns a decrypted value, and that one still requires SecretReadValue.
public static class SecretEndpoints
{
    public static void MapSecretEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/secrets").RequireAuthorization();

        group.MapPost("/", async (
            CreateSecretRequest request,
            ForgeVaultDbContext db,
            IEnvelopeEncryptionService crypto,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var environment = await db.Environments
                .Where(e => e.Id == request.EnvironmentId)
                .Select(e => new { e.Id, e.ProjectId, OrganizationId = e.Project!.OrganizationId })
                .SingleOrDefaultAsync(ct);

            if (environment is null)
            {
                return Results.NotFound(new ErrorResponse("environment_not_found"));
            }

            var actorId = user.GetUserId();
            var scope = new ResourceScope(environment.OrganizationId, environment.ProjectId, environment.Id);
            if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (await db.Secrets.AnyAsync(s => s.EnvironmentId == request.EnvironmentId && s.Name == request.Name, ct))
            {
                return Results.Conflict(new ErrorResponse("secret_name_taken"));
            }

            var now = DateTimeOffset.UtcNow;
            var secret = new Secret
            {
                Id = Guid.NewGuid(),
                EnvironmentId = request.EnvironmentId,
                Name = request.Name,
                Type = request.Type,
                Provider = request.Provider,
                Description = request.Description,
                OwnerId = actorId,
                Status = SecretStatus.Active,
                CurrentVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = request.ExpiresAt,
            };

            var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(request.Value), ct);
            var version = new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                Version = 1,
                Ciphertext = payload.Ciphertext,
                EncryptedDek = payload.EncryptedDek,
                Nonce = payload.Nonce,
                AuthTag = payload.AuthTag,
                Algorithm = payload.Algorithm,
                CreatedBy = actorId,
                CreatedAt = now,
            };

            db.Secrets.Add(secret);
            db.SecretVersions.Add(version);
            db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_CREATE", "secret", secret.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/secrets/{secret.Id}", ToMetadataResponse(secret));
        });

        group.MapGet("/", async (Guid? environmentId, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var query = db.Secrets.AsQueryable();
            if (environmentId is not null)
            {
                query = query.Where(s => s.EnvironmentId == environmentId);
            }

            var secrets = await query.OrderBy(s => s.Name).ToListAsync(ct);
            return Results.Ok(secrets.Select(ToMetadataResponse));
        });

        group.MapGet("/{id:guid}", async (Guid id, ForgeVaultDbContext db, ClaimsPrincipal user, HttpContext http, CancellationToken ct) =>
        {
            var secret = await db.Secrets.FindAsync([id], ct);
            if (secret is null)
            {
                return Results.NotFound();
            }

            db.AuditLogs.Add(AuditLogFactory.Create(user.GetUserId(), user.GetActorType(), "SECRET_READ", "secret", id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToMetadataResponse(secret));
        });

        // Updating the value always creates a new immutable SecretVersion — never an
        // in-place overwrite (docs/ForgeVault.md §22/§106; module 03 §4 invariant 1).
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateSecretValueRequest request,
            ForgeVaultDbContext db,
            IEnvelopeEncryptionService crypto,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var resolved = await ResolveSecretWithScopeAsync(db, id, ct);
            if (resolved is null)
            {
                return Results.NotFound();
            }

            var (secret, scope) = resolved.Value;
            var actorId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
            {
                return Results.Conflict(new ErrorResponse("secret_not_active"));
            }

            var now = DateTimeOffset.UtcNow;
            var newVersionNumber = secret.CurrentVersion + 1;

            var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(request.Value), ct);
            var version = new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                Version = newVersionNumber,
                Ciphertext = payload.Ciphertext,
                EncryptedDek = payload.EncryptedDek,
                Nonce = payload.Nonce,
                AuthTag = payload.AuthTag,
                Algorithm = payload.Algorithm,
                CreatedBy = actorId,
                CreatedAt = now,
            };

            secret.CurrentVersion = newVersionNumber;
            secret.UpdatedAt = now;
            if (request.Description is not null)
            {
                secret.Description = request.Description;
            }

            if (request.ExpiresAt is not null)
            {
                secret.ExpiresAt = request.ExpiresAt;
            }

            db.SecretVersions.Add(version);
            db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_UPDATE", "secret", secret.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToMetadataResponse(secret));
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, ForgeVaultDbContext db, CancellationToken ct) =>
        {
            if (!await db.Secrets.AnyAsync(s => s.Id == id, ct))
            {
                return Results.NotFound();
            }

            var versions = await db.SecretVersions
                .Where(v => v.SecretId == id)
                .OrderByDescending(v => v.Version)
                .ToListAsync(ct);

            // Metadata only — never ciphertext/nonce/auth_tag/encrypted_dek (docs/ForgeVault.md §21).
            return Results.Ok(versions.Select(v => new SecretVersionResponse(v.Id, v.Version, v.Algorithm, v.CreatedBy, v.CreatedAt)));
        });

        // docs/modules/05_ACCESS_BROKER_AND_API.md. `mode` is part of the contract from day
        // one even though REVEAL is the only implemented value — BROKER/SESSION/LEASE/INJECT
        // become additive later without a breaking change (docs/ForgeVault.md §89).
        group.MapGet("/{id:guid}/value", async (
            Guid id,
            string? mode,
            ForgeVaultDbContext db,
            IEnvelopeEncryptionService crypto,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var accessMode = mode ?? "REVEAL";
            if (!string.Equals(accessMode, "REVEAL", StringComparison.Ordinal))
            {
                return Results.Json(new ErrorResponse("unsupported_access_mode"), statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var resolved = await ResolveSecretWithScopeAsync(db, id, ct);
            if (resolved is null)
            {
                return Results.NotFound();
            }

            var (secret, scope) = resolved.Value;
            var actorId = user.GetUserId();
            var actorType = user.GetActorType();
            var requestId = http.TraceIdentifier;

            // Every attempt is audited — including denials (docs/modules/06_AUDIT_AND_GOVERNANCE.md
            // §4 invariant 3 and AC-05: a negation without an audit row is exactly the failure
            // mode this module exists to prevent).
            if (!await permissions.HasPermissionAsync(actorId, Permission.SecretReadValue, scope, ct))
            {
                db.AuditLogs.Add(AuditLogFactory.Create(
                    actorId, actorType, "FAILED_ACCESS", "secret", id, requestId, """{"reason":"forbidden"}"""));
                await db.SaveChangesAsync(ct);
                return Results.Forbid();
            }

            var isExpired = secret.ExpiresAt is not null && secret.ExpiresAt <= DateTimeOffset.UtcNow;
            if (secret.Status is not SecretStatus.Active || isExpired)
            {
                db.AuditLogs.Add(AuditLogFactory.Create(
                    actorId, actorType, "FAILED_ACCESS", "secret", id, requestId, """{"reason":"secret_not_active"}"""));
                await db.SaveChangesAsync(ct);
                return Results.Conflict(new ErrorResponse("secret_not_active"));
            }

            var currentVersion = await db.SecretVersions
                .Where(v => v.SecretId == id && v.Version == secret.CurrentVersion)
                .SingleAsync(ct);

            var payload = new EncryptedPayload(
                currentVersion.Ciphertext, currentVersion.EncryptedDek, currentVersion.Nonce, currentVersion.AuthTag, currentVersion.Algorithm);
            var plaintext = await crypto.DecryptAsync(payload, ct);
            var value = Encoding.UTF8.GetString(plaintext);

            db.AuditLogs.Add(AuditLogFactory.Create(actorId, actorType, "SECRET_REVEAL", "secret", id, requestId));
            await db.SaveChangesAsync(ct);

            var envelope = new CredentialEnvelope(
                RequestId: requestId,
                Identity: actorId.ToString(),
                Resource: secret.Id.ToString(),
                AccessMode: "REVEAL",
                ExpiresAt: secret.ExpiresAt,
                Credentials: new { value },
                Session: null,
                Broker: null,
                Lease: null,
                Metadata: new { });

            return Results.Ok(envelope);
        }).RequireAuthorization("RequireMfa");

        // docs/ForgeVault.md §104 (ROTATE != REVOKE) and §22/§106 (append-only versions).
        // Distinct from PUT above in intent/audit event (SECRET_ROTATE vs SECRET_UPDATE):
        // this is the deliberate "issue a new credential value" operation, not an edit to
        // metadata that happens to also change the value. Provider-driven auto-rotation
        // (PROVIDER_API type, §23) and cascade/impact-analysis on rotate remain module 07
        // (Fase 2) — this is the basic "create version N+1, N stays readable" operation
        // milestone M6 scopes in (docs/architecture/IMPLEMENTATION_READINESS.md §3).
        group.MapPost("/{id:guid}/rotate", async (
            Guid id,
            RotateSecretRequest request,
            ForgeVaultDbContext db,
            IEnvelopeEncryptionService crypto,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var resolved = await ResolveSecretWithScopeAsync(db, id, ct);
            if (resolved is null)
            {
                return Results.NotFound();
            }

            var (secret, scope) = resolved.Value;
            var actorId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
            {
                return Results.Forbid();
            }

            if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
            {
                return Results.Conflict(new ErrorResponse("secret_not_active"));
            }

            var now = DateTimeOffset.UtcNow;
            var newVersionNumber = secret.CurrentVersion + 1;

            var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(request.Value), ct);
            var version = new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                Version = newVersionNumber,
                Ciphertext = payload.Ciphertext,
                EncryptedDek = payload.EncryptedDek,
                Nonce = payload.Nonce,
                AuthTag = payload.AuthTag,
                Algorithm = payload.Algorithm,
                CreatedBy = actorId,
                CreatedAt = now,
            };

            secret.CurrentVersion = newVersionNumber;
            secret.UpdatedAt = now;

            db.SecretVersions.Add(version);
            db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_ROTATE", "secret", secret.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToMetadataResponse(secret));
        });
    }

    private static async Task<(Secret Secret, ResourceScope Scope)?> ResolveSecretWithScopeAsync(
        ForgeVaultDbContext db, Guid secretId, CancellationToken ct)
    {
        var secret = await db.Secrets.FindAsync([secretId], ct);
        if (secret is null)
        {
            return null;
        }

        var chain = await db.Environments
            .Where(e => e.Id == secret.EnvironmentId)
            .Select(e => new { e.ProjectId, OrganizationId = e.Project!.OrganizationId })
            .SingleAsync(ct);

        return (secret, new ResourceScope(chain.OrganizationId, chain.ProjectId, secret.EnvironmentId));
    }

    private static SecretResponse ToMetadataResponse(Secret secret) => new(
        secret.Id,
        secret.EnvironmentId,
        secret.Name,
        secret.Type.ToString(),
        secret.Provider,
        secret.Description,
        secret.Status.ToString(),
        secret.CurrentVersion,
        secret.CreatedAt,
        secret.UpdatedAt,
        secret.ExpiresAt);
}

public sealed record CreateSecretRequest(
    Guid EnvironmentId,
    string Name,
    SecretType Type,
    string? Provider,
    string? Description,
    string Value,
    DateTimeOffset? ExpiresAt);

public sealed record UpdateSecretValueRequest(string Value, string? Description, DateTimeOffset? ExpiresAt);

public sealed record RotateSecretRequest(string Value);

public sealed record SecretResponse(
    Guid Id,
    Guid EnvironmentId,
    string Name,
    string Type,
    string? Provider,
    string? Description,
    string Status,
    int CurrentVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record SecretVersionResponse(Guid Id, int Version, string Algorithm, Guid CreatedBy, DateTimeOffset CreatedAt);

// docs/ForgeVault.md §84 — standardized envelope; `Credentials` is only populated for
// AccessMode == "REVEAL" (the only mode implemented as of M5).
public sealed record CredentialEnvelope(
    string RequestId,
    string Identity,
    string Resource,
    string AccessMode,
    DateTimeOffset? ExpiresAt,
    object? Credentials,
    object? Session,
    object? Broker,
    object? Lease,
    object Metadata);

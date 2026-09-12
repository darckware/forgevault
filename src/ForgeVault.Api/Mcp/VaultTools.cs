using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using ForgeVault.Api.Auditing;
using ForgeVault.Api.Endpoints;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ForgeVault.Api.Mcp;

// M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md). Every tool here is a thin
// wrapper over the exact same services the REST endpoints in Endpoints/ use — same
// IPermissionChecker scope checks, same AuditLogFactory calls, same crypto service. This is
// deliberate: the MCP surface must never become a second, divergent authorization path
// (docs/ForgeVault.md §160 principle 17 / module 09 §12).
//
// `taskId`/`onBehalfOfAgent`/`runtimeSessionRef` accepted below are the ForgeHub context
// fields that survived a real review of ForgeHub's own Task/TaskExecution model (not the
// speculative `project_id` this module's spec originally guessed at — ForgeHub can't
// provide that without an extra lookup of its own). They are stored in AuditLog.Metadata
// for traceability ONLY — never used for authorization. Per-task/agent ABAC remains Fase 3
// (module 09 §1 decision, closing the module's former open_blocking_questions entry).
[McpServerToolType]
public sealed class VaultTools
{
    [McpServerTool(Name = "secret.metadata")]
    [Description("Returns metadata for a secret. Never returns the value — use credential.request for that.")]
    public static async Task<object> SecretMetadataAsync(
        [Description("The secret's id")] Guid secretId,
        ForgeVaultDbContext db,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var secret = await db.Secrets.FindAsync([secretId], ct);
        if (secret is null)
        {
            throw new McpException("secret_not_found");
        }

        db.AuditLogs.Add(AuditLogFactory.Create(
            user.GetUserId(), user.GetActorType(), "SECRET_READ", "secret", secretId, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "credential.request")]
    [Description("Requests a secret's value under RBAC. Only accessMode=REVEAL is implemented — BROKER/SESSION/LEASE/INJECT are Fase 2/3 (docs/ForgeVault.md §89).")]
    public static async Task<CredentialEnvelope> CredentialRequestAsync(
        [Description("The secret's id")] Guid secretId,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Access mode — only REVEAL is implemented today")] string? accessMode = null,
        [Description("ForgeHub task id, for audit traceability only — never used for authorization")] string? taskId = null,
        [Description("Agent/sub-agent identity this request is on behalf of, for audit traceability only")] string? onBehalfOfAgent = null,
        [Description("ForgeHub TaskExecution.runtime_session_ref, for audit traceability only")] string? runtimeSessionRef = null)
    {
        var mode = accessMode ?? "REVEAL";
        if (!string.Equals(mode, "REVEAL", StringComparison.Ordinal))
        {
            throw new McpException("unsupported_access_mode");
        }

        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        var (secret, scope) = resolved.Value;
        var actorId = user.GetUserId();
        var actorType = user.GetActorType();
        var requestId = NewRequestId();
        var contextFields = new[] { ("taskId", taskId), ("onBehalfOfAgent", onBehalfOfAgent), ("runtimeSessionRef", runtimeSessionRef) };

        // Every attempt is audited, including denials — same invariant as the REST reveal
        // endpoint (docs/modules/06_AUDIT_AND_GOVERNANCE.md §4 invariant 3).
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretReadValue, scope, ct))
        {
            db.AuditLogs.Add(AuditLogFactory.Create(
                actorId, actorType, "FAILED_ACCESS", "secret", secretId, requestId,
                AuditMetadata.Build([("reason", "forbidden"), .. contextFields])));
            await db.SaveChangesAsync(ct);
            throw new McpException("forbidden");
        }

        var isExpired = secret.ExpiresAt is not null && secret.ExpiresAt <= DateTimeOffset.UtcNow;
        if (secret.Status is not SecretStatus.Active || isExpired)
        {
            db.AuditLogs.Add(AuditLogFactory.Create(
                actorId, actorType, "FAILED_ACCESS", "secret", secretId, requestId,
                AuditMetadata.Build([("reason", "secret_not_active"), .. contextFields])));
            await db.SaveChangesAsync(ct);
            throw new McpException("secret_not_active");
        }

        var currentVersion = await db.SecretVersions
            .Where(v => v.SecretId == secretId && v.Version == secret.CurrentVersion)
            .SingleAsync(ct);

        var payload = new EncryptedPayload(
            currentVersion.Ciphertext, currentVersion.EncryptedDek, currentVersion.Nonce, currentVersion.AuthTag, currentVersion.Algorithm);
        var plaintext = await crypto.DecryptAsync(payload, ct);
        var value = Encoding.UTF8.GetString(plaintext);

        db.AuditLogs.Add(AuditLogFactory.Create(
            actorId, actorType, "SECRET_REVEAL", "secret", secretId, requestId, AuditMetadata.Build(contextFields)));
        await db.SaveChangesAsync(ct);

        return new CredentialEnvelope(
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
    }

    [McpServerTool(Name = "capability.check")]
    [Description("Checks whether the caller could perform an action on a secret, WITHOUT performing it or revealing anything (docs/ForgeVault.md §138 Policy Simulation, minimal form).")]
    public static async Task<object> CapabilityCheckAsync(
        [Description("The secret id to check against")] Guid secretId,
        [Description("Permission to check: ProjectWrite, EnvironmentWrite, SecretWrite, SecretReadValue, AuditRead")] string permission,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Enum.TryParse<Permission>(permission, ignoreCase: true, out var parsedPermission))
        {
            throw new McpException("unknown_permission");
        }

        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        var allowed = await permissions.HasPermissionAsync(user.GetUserId(), parsedPermission, resolved.Value.Scope, ct);
        return new { allowed };
    }

    [McpServerTool(Name = "admin.secret.create")]
    [Description("Creates a secret and its first version. Value is encrypted before storage — mirrors POST /api/v1/secrets.")]
    public static async Task<SecretResponse> AdminSecretCreateAsync(
        [Description("Environment id the secret belongs to")] Guid environmentId,
        [Description("Secret name, convention PROVIDER_RESOURCE_PURPOSE")] string name,
        [Description("Secret type, e.g. ApiKey, DatabaseCredential, GenericSecret")] string type,
        [Description("The plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        [Description("Provider name, e.g. openai")] string? provider = null,
        string? description = null,
        DateTimeOffset? expiresAt = null)
    {
        if (!Enum.TryParse<SecretType>(type, ignoreCase: true, out var secretType))
        {
            throw new McpException("unknown_secret_type");
        }

        var environment = await db.Environments
            .Where(e => e.Id == environmentId)
            .Select(e => new { e.Id, e.ProjectId, OrganizationId = e.Project!.OrganizationId })
            .SingleOrDefaultAsync(ct);
        if (environment is null)
        {
            throw new McpException("environment_not_found");
        }

        var actorId = user.GetUserId();
        var scope = new ResourceScope(environment.OrganizationId, environment.ProjectId, environment.Id);
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (await db.Secrets.AnyAsync(s => s.EnvironmentId == environmentId && s.Name == name, ct))
        {
            throw new McpException("secret_name_taken");
        }

        var now = DateTimeOffset.UtcNow;
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            EnvironmentId = environmentId,
            Name = name,
            Type = secretType,
            Provider = provider,
            Description = description,
            OwnerId = actorId,
            Status = SecretStatus.Active,
            CurrentVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt,
        };

        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);
        db.Secrets.Add(secret);
        db.SecretVersions.Add(new SecretVersion
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
        });
        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_CREATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.update")]
    [Description("Updates a secret's value, creating a new immutable version — mirrors PUT /api/v1/secrets/{id}.")]
    public static async Task<SecretResponse> AdminSecretUpdateAsync(
        Guid secretId,
        [Description("The new plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? description = null,
        DateTimeOffset? expiresAt = null)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
        {
            throw new McpException("secret_not_active");
        }

        var now = DateTimeOffset.UtcNow;
        var newVersionNumber = secret.CurrentVersion + 1;
        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);

        db.SecretVersions.Add(new SecretVersion
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
        });

        secret.CurrentVersion = newVersionNumber;
        secret.UpdatedAt = now;
        if (description is not null)
        {
            secret.Description = description;
        }

        if (expiresAt is not null)
        {
            secret.ExpiresAt = expiresAt;
        }

        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_UPDATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.rotate")]
    [Description("Issues a new credential value for a secret (SECRET_ROTATE, not SECRET_UPDATE) — mirrors POST /api/v1/secrets/{id}/rotate.")]
    public static async Task<SecretResponse> AdminSecretRotateAsync(
        Guid secretId,
        [Description("The new plaintext value — encrypted immediately, never logged")] string value,
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked or SecretStatus.Archived)
        {
            throw new McpException("secret_not_active");
        }

        var now = DateTimeOffset.UtcNow;
        var newVersionNumber = secret.CurrentVersion + 1;
        var payload = await crypto.EncryptAsync(Encoding.UTF8.GetBytes(value), ct);

        db.SecretVersions.Add(new SecretVersion
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
        });

        secret.CurrentVersion = newVersionNumber;
        secret.UpdatedAt = now;

        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_ROTATE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.secret.revoke")]
    [Description("Revokes a secret — no further read/write/reveal/rotate succeeds. Idempotent. Mirrors POST /api/v1/secrets/{id}/revoke.")]
    public static async Task<SecretResponse> AdminSecretRevokeAsync(
        Guid secretId,
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var (secret, scope) = await ResolveOrThrowAsync(db, secretId, ct);
        var actorId = user.GetUserId();
        if (!await permissions.HasPermissionAsync(actorId, Permission.SecretWrite, scope, ct))
        {
            throw new McpException("forbidden");
        }

        if (secret.Status is SecretStatus.Revoked)
        {
            return SecretScopeResolver.ToMetadataResponse(secret);
        }

        secret.Status = SecretStatus.Revoked;
        secret.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditLogs.Add(AuditLogFactory.Create(actorId, user.GetActorType(), "SECRET_REVOKE", "secret", secret.Id, NewRequestId()));
        await db.SaveChangesAsync(ct);

        return SecretScopeResolver.ToMetadataResponse(secret);
    }

    [McpServerTool(Name = "admin.audit.search")]
    [Description("Searches the audit trail. Never returns secret values — audit rows never contain them. Mirrors GET /api/v1/audit.")]
    public static async Task<AuditLogPage> AdminAuditSearchAsync(
        ForgeVaultDbContext db,
        IPermissionChecker permissions,
        ClaimsPrincipal user,
        CancellationToken ct,
        string? actorId = null,
        string? resourceType = null,
        Guid? resourceId = null,
        string? action = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int? page = null,
        int? pageSize = null)
    {
        if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.AuditRead, ct))
        {
            throw new McpException("forbidden");
        }

        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            query = query.Where(a => a.ActorId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            query = query.Where(a => a.ResourceType == resourceType);
        }

        if (resourceId is not null)
        {
            query = query.Where(a => a.ResourceId == resourceId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (from is not null)
        {
            query = query.Where(a => a.Timestamp >= from);
        }

        if (to is not null)
        {
            query = query.Where(a => a.Timestamp <= to);
        }

        var effectivePage = page is > 0 ? page.Value : 1;
        var effectivePageSize = pageSize is > 0 and <= 200 ? pageSize.Value : 50;

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(a => new AuditLogResponse(
                a.Id, a.ActorId, a.ActorType.ToString(), a.Action, a.ResourceType, a.ResourceId,
                a.RequestId, a.CorrelationId, a.Timestamp, a.Metadata))
            .ToListAsync(ct);

        return new AuditLogPage(items, effectivePage, effectivePageSize, total);
    }

    private static async Task<(Secret Secret, ResourceScope Scope)> ResolveOrThrowAsync(
        ForgeVaultDbContext db, Guid secretId, CancellationToken ct)
    {
        var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
        if (resolved is null)
        {
            throw new McpException("secret_not_found");
        }

        return resolved.Value;
    }

    private static string NewRequestId() => Guid.NewGuid().ToString();
}

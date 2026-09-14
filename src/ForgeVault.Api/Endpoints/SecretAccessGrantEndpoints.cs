using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// Per-credential access grant — the gap between "no access" and "every Secret in this
// RoleAssignment's whole scope" (docs/architecture/IMPLEMENTATION_READINESS.md decision,
// this revision): an Owner/Admin can hand a single agent exactly one credential (a site
// login, a database credential, a provider token) without also handing out everything else
// in the Environment. Gated by SecretWrite at the secret's own scope — whoever can manage
// the secret can also decide who else may read it, same principle RoleAssignmentWrite (M9)
// and McpRegistryWrite (M10) already established for their own resources.
public static class SecretAccessGrantEndpoints
{
    public static void MapSecretAccessGrantEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/secrets/{secretId:guid}/access-grants", async (
            Guid secretId,
            CreateSecretAccessGrantRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
            if (resolved is null)
            {
                return Results.NotFound(new ErrorResponse("secret_not_found"));
            }

            var (_, scope) = resolved.Value;
            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAsync(callerId, Permission.SecretWrite, scope, ct))
            {
                return Results.Forbid();
            }

            // Idempotent: re-granting an already-active grant returns it instead of
            // duplicating (same convention as RoleAssignmentEndpoints).
            var existing = await db.SecretAccessGrants.SingleOrDefaultAsync(
                g => g.SecretId == secretId && g.IdentityId == request.IdentityId && g.RevokedAt == null, ct);
            if (existing is not null)
            {
                return Results.Ok(ToResponse(existing));
            }

            var grant = new SecretAccessGrant
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                IdentityId = request.IdentityId,
                GrantedBy = callerId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.SecretAccessGrants.Add(grant);
            db.AuditLogs.Add(AuditLogFactory.Create(
                callerId, user.GetActorType(), "SECRET_ACCESS_GRANTED", "secret_access_grant", grant.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/secret-access-grants/{grant.Id}", ToResponse(grant));
        }).RequireAuthorization();

        app.MapPost("/api/v1/secret-access-grants/{id:guid}/revoke", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var grant = await db.SecretAccessGrants.FindAsync([id], ct);
            if (grant is null)
            {
                return Results.NotFound();
            }

            var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, grant.SecretId, ct);
            var callerId = user.GetUserId();
            if (resolved is null || !await permissions.HasPermissionAsync(callerId, Permission.SecretWrite, resolved.Value.Scope, ct))
            {
                return Results.Forbid();
            }

            if (grant.RevokedAt is null)
            {
                grant.RevokedAt = DateTimeOffset.UtcNow;
                db.AuditLogs.Add(AuditLogFactory.Create(
                    callerId, user.GetActorType(), "SECRET_ACCESS_REVOKED", "secret_access_grant", grant.Id, http.TraceIdentifier));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(ToResponse(grant));
        }).RequireAuthorization();

        // "Who can read this secret" — governance view, gated the same as managing the
        // secret itself (SecretWrite at its scope).
        app.MapGet("/api/v1/secrets/{secretId:guid}/access-grants", async (
            Guid secretId,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
            if (resolved is null)
            {
                return Results.NotFound(new ErrorResponse("secret_not_found"));
            }

            if (!await permissions.HasPermissionAsync(user.GetUserId(), Permission.SecretWrite, resolved.Value.Scope, ct))
            {
                return Results.Forbid();
            }

            var grants = await db.SecretAccessGrants
                .Where(g => g.SecretId == secretId)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(grants.Select(ToResponse));
        }).RequireAuthorization();

        // "What can this identity read" — same governance-view gating as
        // GET /identities/{id}/role-assignments (RoleAssignmentEndpoints): reused rather
        // than adding a new Permission just for this one read-only listing.
        app.MapGet("/api/v1/identities/{identityId:guid}/secret-access-grants", async (
            Guid identityId,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.RoleAssignmentWrite, ct))
            {
                return Results.Forbid();
            }

            var grants = await db.SecretAccessGrants
                .Where(g => g.IdentityId == identityId)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync(ct);

            return Results.Ok(grants.Select(ToResponse));
        }).RequireAuthorization();
    }

    private static SecretAccessGrantResponse ToResponse(SecretAccessGrant grant) => new(
        grant.Id,
        grant.SecretId,
        grant.IdentityId,
        grant.GrantedBy,
        grant.RevokedAt is null ? "active" : "revoked",
        grant.CreatedAt,
        grant.RevokedAt);
}

public sealed record CreateSecretAccessGrantRequest(Guid IdentityId);

public sealed record SecretAccessGrantResponse(
    Guid Id,
    Guid SecretId,
    Guid IdentityId,
    Guid GrantedBy,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

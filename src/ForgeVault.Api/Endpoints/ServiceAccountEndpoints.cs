using System.Security.Claims;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/ForgeVault.md §31-32, §76 (M7). Like Organization creation, provisioning a Service
// Account has no natural parent scope to inherit RBAC from — it's a platform-level identity,
// not owned by one Organization — so this is [Authorize]-only for now, same documented gap
// as OrganizationEndpoints. A real governance/admin surface for this is future work.
public static class ServiceAccountEndpoints
{
    public static void MapServiceAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/service-accounts").RequireAuthorization();

        // Creating an identity and minting its tokens are identity management: gated by
        // UserManage (Owner/Admin), like DELETE below. Until 2026-10-16 both were
        // [Authorize]-only, so ANY authenticated identity -- any agent's fv_sa_ token --
        // could mint a token for another service account and act with that account's
        // roles (e.g. read every secret an agent's role reaches). Host provisioning uses
        // the local `host-bootstrap` command instead (HostBootstrap.cs).
        group.MapPost("/", async (
            CreateServiceAccountRequest request,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            if (await db.ServiceAccounts.AnyAsync(s => s.Name == request.Name, ct))
            {
                return Results.Conflict(new ErrorResponse("service_account_name_taken"));
            }

            var account = new ServiceAccount
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.ServiceAccounts.Add(account);
            db.AuditLogs.Add(AuditLogFactory.Create(
                user.GetUserId(), user.GetActorType(), "SERVICE_ACCOUNT_CREATE", "service_account", account.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/service-accounts/{account.Id}", ToResponse(account));
        });

        group.MapGet("/", async (ForgeVaultDbContext db, CancellationToken ct) =>
        {
            var accounts = await db.ServiceAccounts.OrderBy(s => s.Name).ToListAsync(ct);
            return Results.Ok(accounts.Select(ToResponse));
        });

        // Issues a brand-new token — the raw value is returned exactly once
        // (docs/ForgeVault.md §115), same discipline as MFA enrollment (M6).
        group.MapPost("/{id:guid}/tokens", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!await permissions.HasPermissionAnywhereAsync(user.GetUserId(), Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var account = await db.ServiceAccounts.FindAsync([id], ct);
            if (account is null)
            {
                return Results.NotFound();
            }

            var rawToken = ServiceAccountTokenFactory.GenerateRawToken();
            var token = new ServiceAccountToken
            {
                Id = Guid.NewGuid(),
                ServiceAccountId = account.Id,
                TokenHash = ServiceAccountTokenFactory.Hash(rawToken),
                TokenPrefix = ServiceAccountTokenFactory.ToDisplayPrefix(rawToken),
                IssuedAt = DateTimeOffset.UtcNow,
            };

            db.ServiceAccountTokens.Add(token);
            db.AuditLogs.Add(AuditLogFactory.Create(
                user.GetUserId(), user.GetActorType(), "TOKEN_ISSUED", "service_account", account.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.Ok(new ServiceAccountTokenResponse(rawToken, token.TokenPrefix, token.IssuedAt));
        });

        // Revoking is allowed to identity admins and to the account itself (an identity
        // can always retire its own tokens); never to an unrelated identity.
        group.MapPost("/{id:guid}/tokens/{tokenId:guid}/revoke", async (
            Guid id,
            Guid tokenId,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (callerId != id && !await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var token = await db.ServiceAccountTokens
                .SingleOrDefaultAsync(t => t.Id == tokenId && t.ServiceAccountId == id, ct);
            if (token is null)
            {
                return Results.NotFound();
            }

            token.RevokedAt ??= DateTimeOffset.UtcNow;
            db.AuditLogs.Add(AuditLogFactory.Create(
                user.GetUserId(), user.GetActorType(), "TOKEN_REVOKED", "service_account", id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });

        // Hard delete of a retired agent/service identity (2026-10-14, Marcelo: "altere o
        // sistema para permitir a exclusão dos serviços no forgevault. preciso excluir o
        // vector"). Mirrors UserEndpoints' DELETE: gated by UserManage (identity management,
        // Owner/Admin), an actor can't delete itself, and every grant the identity holds is
        // revoked first with its own audited action — RoleAssignment, SecretAccessGrant and
        // McpServerAssignment all point at the identity by a soft IdentityId (no FK), so a
        // bare row delete would leave live-looking grants for an identity that no longer
        // exists. Its tokens go with the row (FK cascade); the audit trail stays.
        group.MapDelete("/{id:guid}", async (
            Guid id,
            ForgeVaultDbContext db,
            IPermissionChecker permissions,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
            var callerId = user.GetUserId();
            if (!await permissions.HasPermissionAnywhereAsync(callerId, Permission.UserManage, ct))
            {
                return Results.Forbid();
            }

            var account = await db.ServiceAccounts.FindAsync([id], ct);
            if (account is null)
            {
                return Results.NotFound();
            }

            if (id == callerId)
            {
                return Results.Json(new ErrorResponse("cannot_delete_self"), statusCode: StatusCodes.Status409Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            var actorType = user.GetActorType();

            var roles = await db.RoleAssignments.Where(r => r.IdentityId == id && r.RevokedAt == null).ToListAsync(ct);
            foreach (var assignment in roles)
            {
                assignment.RevokedAt = now;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, actorType, "ACCESS_REVOKED", "role_assignment", assignment.Id, http.TraceIdentifier));
            }

            var grants = await db.SecretAccessGrants.Where(g => g.IdentityId == id && g.RevokedAt == null).ToListAsync(ct);
            foreach (var grant in grants)
            {
                grant.RevokedAt = now;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, actorType, "SECRET_ACCESS_REVOKED", "secret_access_grant", grant.Id, http.TraceIdentifier));
            }

            var mcpAssignments = await db.McpServerAssignments.Where(m => m.IdentityId == id && m.RevokedAt == null).ToListAsync(ct);
            foreach (var assignment in mcpAssignments)
            {
                assignment.RevokedAt = now;
                db.AuditLogs.Add(AuditLogFactory.Create(callerId, actorType, "MCP_SERVER_ASSIGNMENT_REVOKED", "mcp_server_assignment", assignment.Id, http.TraceIdentifier));
            }

            db.ServiceAccounts.Remove(account);
            db.AuditLogs.Add(AuditLogFactory.Create(callerId, actorType, "SERVICE_ACCOUNT_DELETED", "service_account", account.Id, http.TraceIdentifier));
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        });
    }

    private static ServiceAccountResponse ToResponse(ServiceAccount account) => new(
        account.Id, account.Name, account.IsActive, account.CreatedAt);
}

public sealed record CreateServiceAccountRequest(string Name);

public sealed record ServiceAccountResponse(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt);

public sealed record ServiceAccountTokenResponse(string Token, string TokenPrefix, DateTimeOffset IssuedAt);

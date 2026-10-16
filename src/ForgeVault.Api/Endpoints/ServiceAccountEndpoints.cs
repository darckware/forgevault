using System.Security.Claims;
using System.Text.Json;
using ForgeVault.Api.Auditing;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Auth;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api.Endpoints;

// docs/ForgeVault.md §31-32, §76 (M7). A Service Account is a platform-level identity;
// its active role assignments determine which scopes it can reach.
public static class ServiceAccountEndpoints
{
    public static void MapServiceAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/service-accounts").RequireAuthorization();

        // Account creation requires UserManage. Host provisioning uses the local
        // `host-bootstrap` command instead (HostBootstrap.cs).
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

        // Minting requires UserManage across every role, direct grant and MCP scope.
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
            var account = await db.ServiceAccounts.FindAsync([id], ct);
            if (account is null)
            {
                return Results.NotFound();
            }

            if (!await CanManageAccountAsync(db, permissions, user.GetUserId(), id, ct))
            {
                return Results.Forbid();
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

        // Revoking is allowed to in-scope identity admins and to the account itself (an identity
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
            if (callerId != id && !await CanManageAccountAsync(db, permissions, callerId, id, ct))
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
        // vector"). Requires UserManage in every active account scope; an actor can't delete
        // itself, and every grant the identity holds is
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
            var account = await db.ServiceAccounts.FindAsync([id], ct);
            if (account is null)
            {
                return Results.NotFound();
            }

            if (!await CanManageAccountAsync(db, permissions, callerId, id, ct))
            {
                return Results.Forbid();
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

    // A token grants the account's roles, direct secret grants and MCP assignments.
    // The caller must administer every active scope those access paths reach.
    private static async Task<bool> CanManageAccountAsync(
        ForgeVaultDbContext db, IPermissionChecker permissions, Guid callerId, Guid accountId, CancellationToken ct)
    {
        var assignments = await db.RoleAssignments
            .Where(r => r.IdentityId == accountId && r.RevokedAt == null)
            .Select(r => new { r.ScopeType, r.ScopeId })
            .ToListAsync(ct);

        var hasScope = false;

        foreach (var assignment in assignments)
        {
            hasScope = true;
            var scope = await RoleAssignmentScopeResolver.ResolveAsync(db, assignment.ScopeType, assignment.ScopeId, ct);
            if (scope is null || !await permissions.HasPermissionAsync(callerId, Permission.UserManage, scope, ct))
            {
                return false;
            }
        }

        var secretIds = await db.SecretAccessGrants
            .Where(g => g.IdentityId == accountId && g.RevokedAt == null)
            .Select(g => g.SecretId)
            .ToListAsync(ct);
        foreach (var secretId in secretIds)
        {
            hasScope = true;
            var secret = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
            if (secret is null || !await permissions.HasPermissionAsync(callerId, Permission.UserManage, secret.Value.Scope, ct))
            {
                return false;
            }
        }

        var mcpAssignments = await db.McpServerAssignments
            .Where(a => a.IdentityId == accountId && a.RevokedAt == null)
            .Select(a => new { OrganizationId = a.McpServerDefinition!.OrganizationId, a.ParamValuesJson })
            .ToListAsync(ct);
        foreach (var assignment in mcpAssignments)
        {
            hasScope = true;
            if (!await permissions.HasPermissionAsync(callerId, Permission.UserManage,
                    new ResourceScope(assignment.OrganizationId, null, null), ct))
            {
                return false;
            }

            try
            {
                using var parameters = JsonDocument.Parse(assignment.ParamValuesJson);
                if (parameters.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var parameter in parameters.RootElement.EnumerateObject())
                {
                    if (parameter.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    if (!parameter.Value.TryGetProperty("secretId", out var secretIdValue))
                    {
                        continue;
                    }
                    if (secretIdValue.ValueKind != JsonValueKind.String ||
                        !Guid.TryParse(secretIdValue.GetString(), out var secretId))
                    {
                        return false;
                    }
                    var secret = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
                    if (secret is null || !await permissions.HasPermissionAsync(callerId, Permission.UserManage, secret.Value.Scope, ct))
                    {
                        return false;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return hasScope;
    }
}

public sealed record CreateServiceAccountRequest(string Name);

public sealed record ServiceAccountResponse(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt);

public sealed record ServiceAccountTokenResponse(string Token, string TokenPrefix, DateTimeOffset IssuedAt);

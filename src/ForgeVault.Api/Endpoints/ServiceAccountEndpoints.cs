using System.Security.Claims;
using ForgeVault.Api.Auditing;
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

        group.MapPost("/", async (
            CreateServiceAccountRequest request,
            ForgeVaultDbContext db,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
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
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
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

        group.MapPost("/{id:guid}/tokens/{tokenId:guid}/revoke", async (
            Guid id,
            Guid tokenId,
            ForgeVaultDbContext db,
            ClaimsPrincipal user,
            HttpContext http,
            CancellationToken ct) =>
        {
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
    }

    private static ServiceAccountResponse ToResponse(ServiceAccount account) => new(
        account.Id, account.Name, account.IsActive, account.CreatedAt);
}

public sealed record CreateServiceAccountRequest(string Name);

public sealed record ServiceAccountResponse(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt);

public sealed record ServiceAccountTokenResponse(string Token, string TokenPrefix, DateTimeOffset IssuedAt);

using ForgeVault.Api.Endpoints;
using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api;

// Shared by SecretEndpoints (REST) and Mcp/VaultTools (MCP, M8) — both need to resolve a
// Secret's Environment->Project->Organization scope chain for RBAC checks, and both need
// the same metadata response shape. Kept as one small helper instead of two call sites
// silently drifting (docs/architecture/IMPLEMENTATION_READINESS.md M8 decision).
internal static class SecretScopeResolver
{
    public static async Task<(Secret Secret, ResourceScope Scope)?> ResolveWithScopeAsync(
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

    public static SecretResponse ToMetadataResponse(Secret secret) => new(
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

using ForgeVault.Application.Authorization;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api;

// A RoleAssignment (module 04) is scope-wide: an identity granted SecretReadValue at an
// Environment sees every Secret in it. SecretAccessGrant is the narrower alternative — "this
// specific identity may read this specific Secret" — for an agent that should get exactly
// one credential (a site login, a DB credential, a provider token) without also getting
// everything else in the Environment. Same design principle McpServerAssignment already
// uses for self-render (McpAssignmentRenderer: "self-render trusts the assignment"),
// generalized here so it also works against GET /secrets/{id}/value directly, not only MCP
// config rendering. Shared by SecretEndpoints (REST) and VaultTools (MCP) so the two
// surfaces never diverge on who is allowed to read a value.
internal static class SecretAccessAuthorizer
{
    public static async Task<bool> CanReadValueAsync(
        ForgeVaultDbContext db, IPermissionChecker permissions, Guid identityId, Guid secretId, ResourceScope scope, CancellationToken ct)
    {
        if (await permissions.HasPermissionAsync(identityId, Permission.SecretReadValue, scope, ct))
        {
            return true;
        }

        return await db.SecretAccessGrants.AnyAsync(
            g => g.SecretId == secretId && g.IdentityId == identityId && g.RevokedAt == null, ct);
    }
}

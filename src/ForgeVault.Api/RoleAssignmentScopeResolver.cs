using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api;

// docs/modules/04_AUTHORIZATION_AND_POLICY.md §7 (assignRole/revokeRoleAssignment). Granting
// or revoking a RoleAssignment is itself a privileged action scoped to the same
// Organization/Project/Environment hierarchy as everything else — the caller needs
// Permission.RoleAssignmentWrite at (or above) the scope being granted into, exactly like
// SecretScopeResolver resolves a Secret's chain for SecretWrite/SecretReadValue checks.
internal static class RoleAssignmentScopeResolver
{
    public static async Task<ResourceScope?> ResolveAsync(
        ForgeVaultDbContext db, RoleScopeType scopeType, Guid scopeId, CancellationToken ct)
    {
        switch (scopeType)
        {
            case RoleScopeType.Organization:
                var organizationExists = await db.Organizations.AnyAsync(o => o.Id == scopeId, ct);
                return organizationExists ? new ResourceScope(scopeId, null, null) : null;

            case RoleScopeType.Project:
                var project = await db.Projects
                    .Where(p => p.Id == scopeId)
                    .Select(p => new { p.OrganizationId })
                    .SingleOrDefaultAsync(ct);
                return project is null ? null : new ResourceScope(project.OrganizationId, scopeId, null);

            case RoleScopeType.Environment:
                var environment = await db.Environments
                    .Where(e => e.Id == scopeId)
                    .Select(e => new { e.ProjectId, OrganizationId = e.Project!.OrganizationId })
                    .SingleOrDefaultAsync(ct);
                return environment is null
                    ? null
                    : new ResourceScope(environment.OrganizationId, environment.ProjectId, scopeId);

            default:
                return null;
        }
    }
}

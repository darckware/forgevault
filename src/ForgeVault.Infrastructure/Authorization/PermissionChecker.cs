using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Infrastructure.Authorization;

public sealed class PermissionChecker(ForgeVaultDbContext db) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(Guid identityId, Permission permission, ResourceScope scope, CancellationToken ct)
    {
        var roles = await db.RoleAssignments
            .Where(r => r.IdentityId == identityId && r.RevokedAt == null)
            .Where(r =>
                (r.ScopeType == RoleScopeType.Organization && scope.OrganizationId != null && r.ScopeId == scope.OrganizationId) ||
                (r.ScopeType == RoleScopeType.Project && scope.ProjectId != null && r.ScopeId == scope.ProjectId) ||
                (r.ScopeType == RoleScopeType.Environment && scope.EnvironmentId != null && r.ScopeId == scope.EnvironmentId))
            .Select(r => r.Role)
            .ToListAsync(ct);

        return roles.Any(role => RolePermissions.Grants(role, permission));
    }
}

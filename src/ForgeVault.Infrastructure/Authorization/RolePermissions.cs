using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Infrastructure.Authorization;

// First-cut default role -> permission matrix. docs/modules/04_AUTHORIZATION_AND_POLICY.md
// is status: draft and does not (yet) define an exhaustive permission table — this is a
// documented, deliberately conservative starting point, not a finished policy engine:
//
// | Role            | ProjectWrite | EnvironmentWrite | SecretWrite | SecretReadValue | AuditRead | RoleAssignmentWrite | McpRegistryWrite | UserManage | ProjectCreate |
// |-----------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
// | Owner           |  x  |  x  |  x  |  x  |  x  |  x  |  x  |  x  |  x  |
// | Admin           |  x  |  x  |  x  |  x  |  x  |  x  |  x  |  x  |  x  |
// | SecurityAdmin   |     |     |  x  |  x  |  x  |     |     |     |     |
// | ProjectAdmin    |  x  |  x  |  x  |  x  |     |     |     |     |  x  |
// | Developer       |     |     |  x  |  x  |     |     |     |     |     |
// | Operator        |     |     |  x  |     |     |     |     |     |     |
// | Auditor         |     |     |     |     |  x  |     |     |     |     |
// | ReadOnly        |     |     |     |     |     |     |     |     |     |
// | Agent           |     |     |  x  |  x  |     |     |     |     |     |
// | ServiceAccount  |     |     |  x  |  x  |     |     |     |     |     |
// | Provisioner     |     |     |     |     |     |     |     |     |  x  |
//
// ReadOnly never grants anything, matching docs/modules/04_AUTHORIZATION_AND_POLICY.md §4
// invariant 3. Auditor previously granted nothing at all (a gap from M5 — a role whose
// entire purpose is reading the audit trail couldn't actually do so); M8 closes that by
// granting AuditRead, without also granting SecretReadValue/SecretWrite — an auditor reads
// records of what happened, it does not gain the ability to act. RoleAssignmentWrite (M9) is
// restricted to Owner/Admin only — matches docs/modules/04_AUTHORIZATION_AND_POLICY.md §7's
// literal "authorization: role in [OWNER, ADMIN]" for assignRole/revokeRoleAssignment. This
// is a known, deliberate escalation surface: an Owner/Admin at a given scope can grant any
// role (including Owner) at or below that scope, with no additional check that the grantee
// role is "no higher than" the caller's own — the same "first cut, not a definitive policy
// engine" caveat as the rest of this matrix, not a gap specific to this permission.
// McpRegistryWrite (M10) follows the identical Owner/Admin-only pattern as RoleAssignmentWrite.
// UserManage (M13) follows the same Owner/Admin-only pattern — creating a human account is at
// least as sensitive as granting a role, arguably more (it also sets that account's initial
// password).
// ProjectCreate (2026-10-27) goes to every role that already had ProjectWrite, so creating a
// project keeps working for them, plus Provisioner, which has nothing else: granted at
// Organization scope it lets an external agent open a project, and project creation makes it
// Admin of that project only (TenancyProvisioning). Its org-level grant cascades to no
// existing project, secret or role.
internal static class RolePermissions
{
    private static readonly Dictionary<Role, Permission[]> Matrix = new()
    {
        [Role.Owner] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue, Permission.AuditRead, Permission.RoleAssignmentWrite, Permission.McpRegistryWrite, Permission.UserManage, Permission.ProjectCreate],
        [Role.Admin] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue, Permission.AuditRead, Permission.RoleAssignmentWrite, Permission.McpRegistryWrite, Permission.UserManage, Permission.ProjectCreate],
        [Role.SecurityAdmin] = [Permission.SecretWrite, Permission.SecretReadValue, Permission.AuditRead],
        [Role.ProjectAdmin] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue, Permission.ProjectCreate],
        [Role.Developer] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.Operator] = [Permission.SecretWrite],
        [Role.Auditor] = [Permission.AuditRead],
        [Role.ReadOnly] = [],
        [Role.Agent] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.ServiceAccount] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.Provisioner] = [Permission.ProjectCreate],
    };

    public static bool Grants(Role role, Permission permission) =>
        Matrix.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}

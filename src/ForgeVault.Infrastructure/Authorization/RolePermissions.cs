using ForgeVault.Application.Authorization;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Infrastructure.Authorization;

// First-cut default role -> permission matrix. docs/modules/04_AUTHORIZATION_AND_POLICY.md
// is status: draft and does not (yet) define an exhaustive permission table — this is a
// documented, deliberately conservative starting point, not a finished policy engine:
//
// | Role            | ProjectWrite | EnvironmentWrite | SecretWrite | SecretReadValue |
// |-----------------|:---:|:---:|:---:|:---:|
// | Owner           |  x  |  x  |  x  |  x  |
// | Admin           |  x  |  x  |  x  |  x  |
// | SecurityAdmin   |     |     |  x  |  x  |
// | ProjectAdmin    |  x  |  x  |  x  |  x  |
// | Developer       |     |     |  x  |  x  |
// | Operator        |     |     |  x  |     |
// | Auditor         |     |     |     |     |
// | ReadOnly        |     |     |     |     |
// | Agent           |     |     |  x  |  x  |
// | ServiceAccount  |     |     |  x  |  x  |
//
// ReadOnly and Auditor never grant write or reveal, matching
// docs/modules/04_AUTHORIZATION_AND_POLICY.md §4 invariant 3.
internal static class RolePermissions
{
    private static readonly Dictionary<Role, Permission[]> Matrix = new()
    {
        [Role.Owner] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue],
        [Role.Admin] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue],
        [Role.SecurityAdmin] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.ProjectAdmin] = [Permission.ProjectWrite, Permission.EnvironmentWrite, Permission.SecretWrite, Permission.SecretReadValue],
        [Role.Developer] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.Operator] = [Permission.SecretWrite],
        [Role.Auditor] = [],
        [Role.ReadOnly] = [],
        [Role.Agent] = [Permission.SecretWrite, Permission.SecretReadValue],
        [Role.ServiceAccount] = [Permission.SecretWrite, Permission.SecretReadValue],
    };

    public static bool Grants(Role role, Permission permission) =>
        Matrix.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}

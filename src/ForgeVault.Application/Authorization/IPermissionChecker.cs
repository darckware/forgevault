namespace ForgeVault.Application.Authorization;

// docs/modules/04_AUTHORIZATION_AND_POLICY.md. Fine-grained action, not a role name — the
// caller asks "can this identity do X on this resource", never "does this identity have
// role Y" (roles are an internal implementation detail of how permissions are granted).
public enum Permission
{
    ProjectWrite,
    EnvironmentWrite,
    SecretWrite,
    SecretReadValue,
}

// Whichever levels of the hierarchy are known for the resource being checked — a
// RoleAssignment at any populated level grants access (module 04 §17 "Policy
// Inheritance": Organization -> Project -> Environment). A brand-new child resource that
// doesn't exist yet (e.g. checking ProjectWrite before creating a Project) only has
// OrganizationId populated.
public sealed record ResourceScope(Guid? OrganizationId, Guid? ProjectId, Guid? EnvironmentId);

public interface IPermissionChecker
{
    // Default deny: returns false whenever no active RoleAssignment in scope grants the
    // permission (docs/modules/04_AUTHORIZATION_AND_POLICY.md §4 invariant 1).
    Task<bool> HasPermissionAsync(Guid identityId, Permission permission, ResourceScope scope, CancellationToken ct);
}

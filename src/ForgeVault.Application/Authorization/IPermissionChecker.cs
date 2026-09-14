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

    // M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md, admin.audit.search).
    AuditRead,

    // M9 (docs/modules/04_AUTHORIZATION_AND_POLICY.md §7, assignRole/revokeRoleAssignment).
    RoleAssignmentWrite,

    // M10 (docs/modules/11_MCP_REGISTRY.md) — manage the MCP server catalog and assignments.
    McpRegistryWrite,

    // M13 — create/list/deactivate human User accounts (UserEndpoints.cs). Not scoped to an
    // Organization/Project/Environment (a User isn't owned by one — RoleAssignment is what
    // ties a User to a scope), so it's only ever checked with HasPermissionAnywhereAsync,
    // same convention as AuditRead/RoleAssignmentWrite for other cross-cutting governance
    // actions. Before this, the only way to create a human account was a direct INSERT
    // against the database (same gap RoleAssignmentWrite closed for role grants in M9).
    UserManage,
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

    // For cross-cutting governance operations that aren't naturally scoped to one
    // Organization/Project/Environment (M8: admin.audit.search) — true if ANY active
    // RoleAssignment for this identity, at any scope, grants the permission.
    Task<bool> HasPermissionAnywhereAsync(Guid identityId, Permission permission, CancellationToken ct);

    // M14 (docs/modules/07_LIFECYCLE_ROTATION_REVOCATION.md §108/§109, impact analysis):
    // "who currently holds this permission in this scope" — the RoleAssignment-derived half
    // of "who consumes this secret", the other half being SecretAccessGrant/
    // McpServerAssignment rows (which reference a Secret directly and don't need a
    // permission check to enumerate).
    Task<List<(Guid IdentityId, string Role)>> ListIdentitiesWithPermissionAsync(Permission permission, ResourceScope scope, CancellationToken ct);
}

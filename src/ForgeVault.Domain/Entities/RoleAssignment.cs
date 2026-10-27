namespace ForgeVault.Domain.Entities;

// docs/modules/04_AUTHORIZATION_AND_POLICY.md §4.
public enum Role
{
    Owner,
    Admin,
    SecurityAdmin,
    ProjectAdmin,
    Developer,
    Operator,
    Auditor,
    ReadOnly,
    Agent,
    ServiceAccount,
}

public enum RoleScopeType
{
    Organization,
    Project,
    Environment,
}

public sealed class RoleAssignment
{
    public Guid Id { get; set; }

    // Soft reference to an identity — only User (human) identities exist as of M5
    // (docs/modules/02_IDENTITY_AND_AUTHENTICATION.md); this gains a proper polymorphic
    // identity reference once agent/service identities exist (module 09).
    public Guid IdentityId { get; set; }

    public Role Role { get; set; }
    public RoleScopeType ScopeType { get; set; }
    public Guid ScopeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

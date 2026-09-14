namespace ForgeVault.Domain.Entities;

// "Give agent X access to credential Y specifically" — the gap RoleAssignment (scope-wide:
// Organization/Project/Environment) never covered: every identity holding a role in a scope
// sees every Secret in it, with no way to hand out a single credential without also handing
// out the rest of the Environment. Same design already used internally by
// McpServerAssignment (self-render trusts the assignment, no separate SecretReadValue check)
// — this generalizes that pattern to a standalone grant usable outside MCP rendering, e.g.
// against GET /secrets/{id}/value directly.
public sealed class SecretAccessGrant
{
    public Guid Id { get; set; }
    public Guid SecretId { get; set; }

    // Soft reference to an identity (User or ServiceAccount) — same convention as
    // RoleAssignment.IdentityId / McpServerAssignment.IdentityId.
    public Guid IdentityId { get; set; }

    public Guid GrantedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public Secret? Secret { get; set; }
}

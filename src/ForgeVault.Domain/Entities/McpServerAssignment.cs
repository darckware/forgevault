namespace ForgeVault.Domain.Entities;

// M10 (docs/modules/11_MCP_REGISTRY.md). The binding: "identity Y uses MCP server X, with
// these parameter values." ParamValuesJson is a JSON dict where each value is either a plain
// string (non-sensitive, e.g. an agent slug) or {"secretId": "<guid>"} referencing an
// existing Secret — the actual sensitive value is never duplicated here, only resolved
// (decrypted) on demand at render time via the same IEnvelopeEncryptionService
// credential.request already uses.
public sealed class McpServerAssignment
{
    public Guid Id { get; set; }

    // Soft reference to an identity (User or ServiceAccount) — same convention as
    // RoleAssignment.IdentityId.
    public Guid IdentityId { get; set; }

    public Guid McpServerDefinitionId { get; set; }
    public required string ParamValuesJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public McpServerDefinition? McpServerDefinition { get; set; }
}

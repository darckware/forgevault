namespace ForgeVault.Domain.Entities;

// M10 (docs/modules/11_MCP_REGISTRY.md). The catalog: "there exists an MCP server named X,
// and this is how you connect to it" — never a secret value itself, only the shape of the
// connection. Scoped to an Organization, same pattern as Project/Secret.
public enum McpTransportType
{
    Stdio,
    Http,
}

public sealed class McpServerDefinition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public McpTransportType Transport { get; set; }

    // Stdio transport.
    public string? Command { get; set; }
    public string? ArgsJson { get; set; }

    // Http transport.
    public string? Url { get; set; }
    public int? Timeout { get; set; }
    public int? ConnectTimeout { get; set; }

    // Non-sensitive config shared by every identity assigned this definition (e.g.
    // FORGEHUB_API_URL) — a plain JSON string->string dict.
    public string? StaticEnvJson { get; set; }

    // Names of parameters that MUST be supplied per-assignment as a reference to an existing
    // Secret (e.g. ["FORGEHUB_AGENT_TOKEN"], or ["Authorization"] for an Http definition's
    // header) — never stored here, only declared as required.
    public string? SecretParamNamesJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Organization? Organization { get; set; }
}

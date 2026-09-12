using System.Text.Json;

namespace ForgeVault.Api.Auditing;

// Builds AuditLog.Metadata JSON safely from caller-supplied values (M8: MCP tool
// arguments like taskId/onBehalfOfAgent are attacker/caller-controlled — never
// string-interpolate them directly into a JSON literal, always serialize properly).
internal static class AuditMetadata
{
    public static string Build(params (string Key, string? Value)[] fields)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (key, value) in fields)
        {
            if (value is not null)
            {
                dict[key] = value;
            }
        }

        return JsonSerializer.Serialize(dict);
    }
}

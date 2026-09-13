using System.Text.Json;
using ForgeVault.Domain.Entities;

namespace ForgeVault.Api;

// docs/modules/11_MCP_REGISTRY.md §4 invariant 3 (gap closed here): SecretParamNamesJson on a
// McpServerDefinition was purely declarative — nothing checked, when creating or updating a
// McpServerAssignment, that every name it lists actually shows up in ParamValuesJson as a
// {"secretId": ...} reference. An assignment could be created missing a required parameter
// entirely, or with a "must-be-a-secret" value supplied as a literal string. Shared by the
// REST endpoint and the `admin.mcp.assign` tool for the same reason McpAssignmentRenderer is
// shared: this is exactly the kind of check that must not silently drift between the two.
internal static class McpAssignmentValidation
{
    public static string? FindMissingOrInvalidSecretParam(McpServerDefinition definition, JsonElement paramValues)
    {
        if (definition.SecretParamNamesJson is null)
        {
            return null;
        }

        var requiredNames = JsonSerializer.Deserialize<List<string>>(definition.SecretParamNamesJson)!;
        foreach (var name in requiredNames)
        {
            if (!paramValues.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("secretId", out _))
            {
                return name;
            }
        }

        return null;
    }
}

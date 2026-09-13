using System.Text;
using System.Text.Json;
using ForgeVault.Api.Auditing;
using ForgeVault.Api.Endpoints;
using ForgeVault.Application.Authorization;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ForgeVault.Api;

// M10 (docs/modules/11_MCP_REGISTRY.md). Shared by the REST render endpoint and the
// `mcp.render_config` MCP tool — resolving an assignment's ParamValuesJson (some values
// literal, some {"secretId": ...} references) and decrypting each referenced secret is
// identical logic on both surfaces, and it is exactly the kind of security-sensitive code
// this project never wants to fork between REST and MCP (module 09 §12 principle, reused
// here for a different feature).
internal enum McpRenderOutcome
{
    Success,
    AssignmentNotFound,
    DefinitionNotFound,
    ReferencedSecretNotFound,
    Forbidden,
    AssignmentRevoked,
}

internal sealed record McpRenderResult(McpRenderOutcome Outcome, McpServerRenderResponse? Response, string? Detail);

internal static class McpAssignmentRenderer
{
    public static async Task<McpRenderResult> RenderAsync(
        ForgeVaultDbContext db,
        IEnvelopeEncryptionService crypto,
        IPermissionChecker permissions,
        Guid assignmentId,
        Guid callerId,
        AuditActorType callerType,
        string requestId,
        CancellationToken ct)
    {
        var assignment = await db.McpServerAssignments.FindAsync([assignmentId], ct);
        if (assignment is null)
        {
            return new McpRenderResult(McpRenderOutcome.AssignmentNotFound, null, null);
        }

        // docs/modules/11_MCP_REGISTRY.md §12 gap, closed here: revoking an assignment must
        // stop /render and mcp.render_config from resolving it, not just block future
        // create/update calls. Checked before any secret is touched, and audited the same way
        // SecretEndpoints treats a revoked/expired secret (FAILED_ACCESS, distinct reason).
        if (assignment.RevokedAt is not null)
        {
            db.AuditLogs.Add(AuditLogFactory.Create(
                callerId, callerType, "FAILED_ACCESS", "mcp_server_assignment", assignmentId, requestId,
                """{"reason":"assignment_revoked","context":"mcp_render"}"""));
            await db.SaveChangesAsync(ct);
            return new McpRenderResult(McpRenderOutcome.AssignmentRevoked, null, null);
        }

        var definition = await db.McpServerDefinitions.FindAsync([assignment.McpServerDefinitionId], ct);
        if (definition is null)
        {
            return new McpRenderResult(McpRenderOutcome.DefinitionNotFound, null, null);
        }

        var isSelf = callerId == assignment.IdentityId;
        var resolvedValues = new Dictionary<string, string>();

        using var paramDoc = JsonDocument.Parse(assignment.ParamValuesJson);
        foreach (var property in paramDoc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                resolvedValues[property.Name] = property.Value.GetString()!;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("secretId", out var secretIdElement))
            {
                continue;
            }

            var secretId = secretIdElement.GetGuid();
            var resolved = await SecretScopeResolver.ResolveWithScopeAsync(db, secretId, ct);
            if (resolved is null)
            {
                return new McpRenderResult(McpRenderOutcome.ReferencedSecretNotFound, null, secretId.ToString());
            }

            var (secret, scope) = resolved.Value;

            // Self-render trusts the assignment (an Owner/Admin with McpRegistryWrite already
            // decided this identity should have this value when creating it); rendering on
            // someone else's behalf still requires holding SecretReadValue on every
            // referenced secret, same as credential.request.
            if (!isSelf && !await permissions.HasPermissionAsync(callerId, Permission.SecretReadValue, scope, ct))
            {
                db.AuditLogs.Add(AuditLogFactory.Create(
                    callerId, callerType, "FAILED_ACCESS", "secret", secretId, requestId,
                    """{"reason":"forbidden","context":"mcp_render"}"""));
                await db.SaveChangesAsync(ct);
                return new McpRenderResult(McpRenderOutcome.Forbidden, null, null);
            }

            var currentVersion = await db.SecretVersions
                .Where(v => v.SecretId == secretId && v.Version == secret.CurrentVersion)
                .SingleAsync(ct);
            var payload = new EncryptedPayload(currentVersion.Ciphertext, currentVersion.EncryptedDek, currentVersion.Nonce, currentVersion.AuthTag, currentVersion.Algorithm);
            var plaintext = await crypto.DecryptAsync(payload, ct);
            resolvedValues[property.Name] = Encoding.UTF8.GetString(plaintext);

            db.AuditLogs.Add(AuditLogFactory.Create(
                callerId, callerType, "SECRET_REVEAL", "secret", secretId, requestId, """{"context":"mcp_render"}"""));
        }

        if (definition.StaticEnvJson is not null)
        {
            var staticEnv = JsonSerializer.Deserialize<Dictionary<string, string>>(definition.StaticEnvJson)!;
            foreach (var (key, value) in staticEnv)
            {
                resolvedValues.TryAdd(key, value);
            }
        }

        await db.SaveChangesAsync(ct);

        var args = definition.ArgsJson is null ? null : JsonSerializer.Deserialize<List<string>>(definition.ArgsJson);
        var response = new McpServerRenderResponse(
            definition.Name,
            definition.Transport.ToString(),
            definition.Command,
            args,
            definition.Url,
            definition.Timeout,
            definition.ConnectTimeout,
            definition.Transport == McpTransportType.Http ? null : resolvedValues,
            definition.Transport == McpTransportType.Http ? resolvedValues : null);

        return new McpRenderResult(McpRenderOutcome.Success, response, null);
    }
}

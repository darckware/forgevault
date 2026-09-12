using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.E2E.Tests;

// M8 (docs/modules/09_FORGEHUB_FORGEROUTER_MCP_INTEGRATION.md): the MCP surface at /mcp must
// enforce the exact same RBAC + audit invariants as the REST API, since VaultTools reuses the
// same IPermissionChecker/AuditLogFactory calls. These tests are the MCP-shaped equivalent of
// RbacRevealAuditTests (ForgeVault.Security.Tests) — authorized calls succeed and audit,
// unauthorized calls are denied and still audit, and no response or audit row ever leaks a
// secret value.
public sealed class McpToolsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ToolsList_ReturnsAllEightPlannedTools()
    {
        var (client, ownerId) = await CreateAuthenticatedClientAsync();
        _ = ownerId;

        var names = await ListToolNamesAsync(client);

        Assert.Equal(
            new[]
            {
                "admin.audit.search", "admin.secret.create", "admin.secret.revoke", "admin.secret.rotate",
                "admin.secret.update", "capability.check", "credential.request", "secret.metadata",
            },
            names.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Mcp_WithoutBearerToken_Is401()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonRpcContent("tools/list", new { }) };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminSecretCreate_ThenCredentialRequest_Authorized_RevealsValue_AndAudits()
    {
        const string secretValue = "sk-mcp-e2e-do-not-leak-7a21";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var environmentId = await CreateOrgProjectEnvironmentAsync(owner, ownerId);

        var (createIsError, createText) = await CallToolAsync(owner, "admin.secret.create", new
        {
            environmentId,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            value = secretValue,
        });
        Assert.False(createIsError);
        var created = JsonDocument.Parse(createText!).RootElement;
        var secretId = created.GetProperty("id").GetGuid();
        Assert.Equal("Active", created.GetProperty("status").GetString());

        var (revealIsError, revealText) = await CallToolAsync(owner, "credential.request", new
        {
            secretId,
            taskId = "task-42",
            onBehalfOfAgent = "agent-hermes",
            runtimeSessionRef = "sess-1",
        });
        Assert.False(revealIsError);
        var envelope = JsonDocument.Parse(revealText!).RootElement;
        Assert.Equal("REVEAL", envelope.GetProperty("accessMode").GetString());
        Assert.Equal(secretValue, envelope.GetProperty("credentials").GetProperty("value").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();

        var revealLog = await db.AuditLogs.SingleAsync(a => a.ResourceId == secretId && a.Action == "SECRET_REVEAL");
        Assert.Contains("task-42", revealLog.Metadata, StringComparison.Ordinal);
        Assert.Contains("agent-hermes", revealLog.Metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(secretValue, revealLog.Metadata, StringComparison.Ordinal);

        var createLog = await db.AuditLogs.SingleAsync(a => a.ResourceId == secretId && a.Action == "SECRET_CREATE");
        Assert.DoesNotContain(secretValue, createLog.Metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialRequest_Unauthorized_Denies_AndAudits_NoLeak()
    {
        const string secretValue = "sk-mcp-e2e-stranger-cannot-see-this";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var environmentId = await CreateOrgProjectEnvironmentAsync(owner, ownerId);

        var createResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        var (isError, text) = await CallToolAsync(stranger, "credential.request", new { secretId = secret!.Id });

        Assert.True(isError);
        Assert.Contains("forbidden", text, StringComparison.Ordinal);
        Assert.DoesNotContain(secretValue, text!, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var deniedLog = await db.AuditLogs.SingleAsync(a => a.ResourceId == secret.Id && a.Action == "FAILED_ACCESS");
        Assert.DoesNotContain(secretValue, deniedLog.Metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminSecretRevoke_BlocksSubsequentCredentialRequestAndRotate_ButIsIdempotent()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var environmentId = await CreateOrgProjectEnvironmentAsync(owner, ownerId);

        var createResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = "sk-mcp-e2e-revoke-me",
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        var (revokeIsError, revokeText) = await CallToolAsync(owner, "admin.secret.revoke", new { secretId = secret!.Id });
        Assert.False(revokeIsError);
        Assert.Equal("Revoked", JsonDocument.Parse(revokeText!).RootElement.GetProperty("status").GetString());

        var (rereadIsError, _) = await CallToolAsync(owner, "credential.request", new { secretId = secret.Id });
        Assert.True(rereadIsError);

        var (rotateIsError, rotateText) = await CallToolAsync(owner, "admin.secret.rotate", new { secretId = secret.Id, value = "sk-new-value" });
        Assert.True(rotateIsError);
        Assert.Contains("secret_not_active", rotateText, StringComparison.Ordinal);

        // Idempotent: revoking an already-revoked secret is not an error.
        var (secondRevokeIsError, _) = await CallToolAsync(owner, "admin.secret.revoke", new { secretId = secret.Id });
        Assert.False(secondRevokeIsError);
    }

    [Fact]
    public async Task AdminAuditSearch_DeniesReadOnlyRole_AllowsAuditor_NeverLeaksSecretValues()
    {
        const string secretValue = "sk-mcp-e2e-audit-search-secret";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (readOnly, readOnlyId) = await CreateAuthenticatedClientAsync();
        var (auditor, auditorId) = await CreateAuthenticatedClientAsync();

        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);
        await GrantRoleAsync(readOnlyId, Role.ReadOnly, RoleScopeType.Organization, organization.Id);
        await GrantRoleAsync(auditorId, Role.Auditor, RoleScopeType.Organization, organization.Id);

        var projectResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();
        var environmentResponse = await owner.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "OPENAI_API_KEY",
            type = "ApiKey",
            provider = "openai",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createResponse.Content.ReadFromJsonAsync<IdResponse>();

        // ReadOnly has no AuditRead — corrects the pre-M8 gap where Auditor (and everyone
        // else) had zero permissions; this proves the matrix actually denies non-Auditor roles.
        var (deniedIsError, deniedText) = await CallToolAsync(readOnly, "admin.audit.search", new { resourceId = secret!.Id });
        Assert.True(deniedIsError);
        Assert.Contains("forbidden", deniedText, StringComparison.Ordinal);

        var (allowedIsError, allowedText) = await CallToolAsync(auditor, "admin.audit.search", new { resourceId = secret.Id });
        Assert.False(allowedIsError);
        Assert.DoesNotContain(secretValue, allowedText!, StringComparison.Ordinal);

        var page = JsonDocument.Parse(allowedText!).RootElement;
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
    }

    private static async Task<List<string>> ListToolNamesAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonRpcContent("tools/list", new { }) };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var root = await ParseSseJsonRpcResponseAsync(response);

        return root.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .ToList();
    }

    private static async Task<(bool IsError, string? Text)> CallToolAsync(HttpClient client, string toolName, object arguments)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonRpcContent("tools/call", new { name = toolName, arguments }),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var root = await ParseSseJsonRpcResponseAsync(response);

        var result = root.GetProperty("result");
        var isError = result.TryGetProperty("isError", out var errorProp) && errorProp.GetBoolean();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        return (isError, text);
    }

    private static async Task<JsonElement> ParseSseJsonRpcResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var dataLine = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Single(line => line.StartsWith("data:", StringComparison.Ordinal));

        return JsonDocument.Parse(dataLine["data:".Length..].Trim()).RootElement;
    }

    private static StringContent JsonRpcContent(string method, object @params) =>
        new(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params }), System.Text.Encoding.UTF8, "application/json");

    private async Task<Guid> CreateOrgProjectEnvironmentAsync(HttpClient client, Guid ownerId)
    {
        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        return environment!.Id;
    }

    private async Task GrantRoleAsync(Guid identityId, Role role, RoleScopeType scopeType, Guid scopeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();

        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Role = role,
            ScopeType = scopeType,
            ScopeId = scopeId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";
        var userId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTimeOffset.UtcNow;

            db.Users.Add(new User
            {
                Id = userId,
                Email = email,
                PasswordHash = hasher.Hash(password),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, userId);
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record IdResponse(Guid Id);
}

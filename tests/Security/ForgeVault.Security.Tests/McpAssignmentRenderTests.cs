using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// docs/modules/11_MCP_REGISTRY.md §12 — regression for the confirmed gap: revoking a
// McpServerAssignment blocked create/update but not /render, so anyone holding the
// assignmentId could keep pulling the resolved (decrypted) secret after revoke. Fixed in
// McpAssignmentRenderer.RenderAsync by checking RevokedAt before touching any secret.
public sealed class McpAssignmentRenderTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Render_AfterRevoke_Is409_DoesNotLeakSecret_AndAudits()
    {
        const string secretValue = "sk-mcp-render-after-revoke-do-not-leak";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (agent, agentId) = await CreateAuthenticatedClientAsync();

        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var projectResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organization.Id}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();

        var environmentResponse = await owner.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        // The assignee needs at least one active RoleAssignment somewhere (module 10
        // invariant); the render itself is trusted as "self" regardless of RBAC scope.
        await GrantRoleAsync(agentId, Role.Developer, RoleScopeType.Environment, environment!.Id);

        var createSecretResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment.Id,
            name = "FORGEHUB_AGENT_TOKEN",
            type = "ApiKey",
            provider = "forgehub",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await createSecretResponse.Content.ReadFromJsonAsync<IdResponse>();

        var definitionResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/mcp-servers", new
        {
            name = $"forgehub-{Guid.NewGuid():N}",
            transport = "Stdio",
            command = "forgehub-mcp-server",
            args = (List<string>?)null,
            url = (string?)null,
            timeout = (int?)null,
            connectTimeout = (int?)null,
            staticEnv = (Dictionary<string, string>?)null,
            secretParamNames = new[] { "FORGEHUB_AGENT_TOKEN" },
        });
        Assert.Equal(HttpStatusCode.Created, definitionResponse.StatusCode);
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        var assignResponse = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition!.Id,
            paramValues = new Dictionary<string, object> { ["FORGEHUB_AGENT_TOKEN"] = new { secretId = secret!.Id } },
        });
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);
        var assignment = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();

        // Before revoke: the assigned agent can render and gets the decrypted value.
        var firstRender = await agent.GetAsync($"/api/v1/mcp-assignments/{assignment!.Id}/render");
        Assert.Equal(HttpStatusCode.OK, firstRender.StatusCode);
        var rendered = await firstRender.Content.ReadFromJsonAsync<RenderResponseDto>();
        Assert.Equal(secretValue, rendered!.Env!["FORGEHUB_AGENT_TOKEN"]);

        var revokeResponse = await owner.PostAsync($"/api/v1/mcp-assignments/{assignment.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        // The exact repro from the module spec: the same assignmentId, same caller, called
        // again right after revoke. Must no longer resolve — 409, not 200 — and the body must
        // never contain the plaintext secret.
        var secondRender = await agent.GetAsync($"/api/v1/mcp-assignments/{assignment.Id}/render");
        Assert.Equal(HttpStatusCode.Conflict, secondRender.StatusCode);
        var secondBody = await secondRender.Content.ReadAsStringAsync();
        Assert.Contains("assignment_revoked", secondBody, StringComparison.Ordinal);
        Assert.DoesNotContain(secretValue, secondBody, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var deniedLog = await db.AuditLogs.SingleAsync(a =>
            a.ResourceId == assignment.Id && a.Action == "FAILED_ACCESS" && a.ResourceType == "mcp_server_assignment");
        Assert.Contains("assignment_revoked", deniedLog.Metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(secretValue, deniedLog.Metadata, StringComparison.Ordinal);
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

    private sealed record RenderResponseDto(
        string Name,
        string Transport,
        string? Command,
        List<string>? Args,
        string? Url,
        int? Timeout,
        int? ConnectTimeout,
        Dictionary<string, string>? Env,
        Dictionary<string, string>? Headers);
}

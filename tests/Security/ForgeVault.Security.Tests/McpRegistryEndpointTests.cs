using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// docs/modules/11_MCP_REGISTRY.md §13 gap: this module had no dedicated test file — only
// McpToolsTests.ToolsList_ReturnsAllPlannedTools (E2E) touched it at all. These cover AC-01,
// AC-02, AC-03 and AC-05 from that section's acceptance table (AC-04 partial coverage lives in
// McpAssignmentRenderTests, AC-06 in McpToolsTests).
public sealed class McpRegistryEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task RegisterDefinition_RequiresMcpRegistryWrite_AndAuditsOnSuccess()
    {
        // AC-02 (register leg) + AC-01.
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();

        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization!.Id);

        var definitionRequest = new
        {
            name = $"forgehub-{Guid.NewGuid():N}",
            transport = "Stdio",
            command = "forgehub-mcp-server",
            args = (List<string>?)null,
            url = (string?)null,
            timeout = (int?)null,
            connectTimeout = (int?)null,
            staticEnv = (Dictionary<string, string>?)null,
            secretParamNames = (List<string>?)null,
        };

        var deniedResponse = await stranger.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/mcp-servers", definitionRequest);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var allowedResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organization.Id}/mcp-servers", definitionRequest);
        Assert.Equal(HttpStatusCode.Created, allowedResponse.StatusCode);
        var definition = await allowedResponse.Content.ReadFromJsonAsync<IdResponse>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var registeredLog = await db.AuditLogs.SingleAsync(a => a.ResourceId == definition!.Id && a.Action == "MCP_SERVER_REGISTERED");
        Assert.Equal(ownerId.ToString(), registeredLog.ActorId);
    }

    [Fact]
    public async Task NonPrivilegedIdentity_CannotRemoveDefinition_AssignOrRevoke()
    {
        // AC-02, the remove/assign/revoke legs (register leg covered above).
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (_, agentId) = await CreateAuthenticatedClientAsync();

        var (organizationId, environmentId) = await CreateOrgAndEnvironmentAsync(owner, ownerId);
        await GrantRoleAsync(agentId, Role.Developer, RoleScopeType.Environment, environmentId);

        var definitionResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/mcp-servers", new
        {
            name = $"forgehub-{Guid.NewGuid():N}",
            transport = "Stdio",
            command = "forgehub-mcp-server",
            args = (List<string>?)null,
            url = (string?)null,
            timeout = (int?)null,
            connectTimeout = (int?)null,
            staticEnv = (Dictionary<string, string>?)null,
            secretParamNames = (List<string>?)null,
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        var deniedRemove = await stranger.DeleteAsync($"/api/v1/mcp-servers/{definition!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedRemove.StatusCode);

        var deniedAssign = await stranger.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition.Id,
            paramValues = new Dictionary<string, object>(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedAssign.StatusCode);

        var assignResponse = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition.Id,
            paramValues = new Dictionary<string, object>(),
        });
        var assignment = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();

        var deniedRevoke = await stranger.PostAsync($"/api/v1/mcp-assignments/{assignment!.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRevoke.StatusCode);
    }

    [Fact]
    public async Task CreateAssignment_ForIdentityWithNoActiveRoleAssignment_Is409()
    {
        // AC-03.
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (_, identityWithNoRoles) = await CreateAuthenticatedClientAsync();

        var (organizationId, _) = await CreateOrgAndEnvironmentAsync(owner, ownerId);

        var definitionResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/mcp-servers", new
        {
            name = $"forgehub-{Guid.NewGuid():N}",
            transport = "Stdio",
            command = "forgehub-mcp-server",
            args = (List<string>?)null,
            url = (string?)null,
            timeout = (int?)null,
            connectTimeout = (int?)null,
            staticEnv = (Dictionary<string, string>?)null,
            secretParamNames = (List<string>?)null,
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        var response = await owner.PostAsJsonAsync($"/api/v1/identities/{identityWithNoRoles}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition!.Id,
            paramValues = new Dictionary<string, object>(),
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("identity_has_no_role_assignment", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_ByStrangerWithoutSecretReadValue_Is403_AndAudits_NoLeak()
    {
        // AC-05.
        const string secretValue = "sk-mcp-ac05-do-not-leak";

        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (_, agentId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();

        var (organizationId, environmentId) = await CreateOrgAndEnvironmentAsync(owner, ownerId);
        await GrantRoleAsync(agentId, Role.Developer, RoleScopeType.Environment, environmentId);

        var secretResponse = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId,
            name = "FORGEHUB_AGENT_TOKEN",
            type = "ApiKey",
            provider = "forgehub",
            description = (string?)null,
            value = secretValue,
            expiresAt = (DateTimeOffset?)null,
        });
        var secret = await secretResponse.Content.ReadFromJsonAsync<IdResponse>();

        var definitionResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/mcp-servers", new
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
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        var assignResponse = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition!.Id,
            paramValues = new Dictionary<string, object> { ["FORGEHUB_AGENT_TOKEN"] = new { secretId = secret!.Id } },
        });
        var assignment = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();

        // The stranger is neither the assigned identity nor holds SecretReadValue on the
        // referenced secret — render must deny, not just "render for self".
        var renderResponse = await stranger.GetAsync($"/api/v1/mcp-assignments/{assignment!.Id}/render");
        Assert.Equal(HttpStatusCode.Forbidden, renderResponse.StatusCode);
        var body = await renderResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secretValue, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var deniedLog = await db.AuditLogs.SingleAsync(a =>
            a.ResourceId == secret.Id && a.Action == "FAILED_ACCESS" && a.ResourceType == "secret");
        Assert.DoesNotContain(secretValue, deniedLog.Metadata, StringComparison.Ordinal);
    }

    private async Task<(Guid OrganizationId, Guid EnvironmentId)> CreateOrgAndEnvironmentAsync(HttpClient owner, Guid ownerId)
    {
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

        return (organization.Id, environment!.Id);
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

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// docs/modules/11_MCP_REGISTRY.md §4 invariant 3 (gap closed here): McpServerDefinition
// .SecretParamNamesJson was purely declarative — nothing checked, on creating or updating a
// McpServerAssignment, that every name it lists actually appears in ParamValuesJson as a
// {"secretId": ...} reference. Fixed via McpAssignmentValidation.FindMissingOrInvalidSecretParam,
// shared by the REST endpoint and the `admin.mcp.assign` tool.
public sealed class McpAssignmentValidationTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task CreateAssignment_MissingRequiredSecretParam_Is409_AndNotPersisted()
    {
        var (owner, definitionId, agentId) = await SetupDefinitionRequiringSecretParamAsync();

        var response = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definitionId,
            paramValues = new Dictionary<string, object>(), // FORGEHUB_AGENT_TOKEN entirely absent.
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("missing_required_secret_param:FORGEHUB_AGENT_TOKEN", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        Assert.False(await db.McpServerAssignments.AnyAsync(a => a.IdentityId == agentId));
    }

    [Fact]
    public async Task CreateAssignment_RequiredSecretParamSuppliedAsLiteralString_Is409_AndNotPersisted()
    {
        var (owner, definitionId, agentId) = await SetupDefinitionRequiringSecretParamAsync();

        var response = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definitionId,
            // Declares the value directly instead of referencing a Secret — exactly the second
            // failure mode invariant 3 calls out.
            paramValues = new Dictionary<string, object> { ["FORGEHUB_AGENT_TOKEN"] = "hardcoded-not-a-secret-reference" },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("missing_required_secret_param:FORGEHUB_AGENT_TOKEN", body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        Assert.False(await db.McpServerAssignments.AnyAsync(a => a.IdentityId == agentId));
    }

    private async Task<(HttpClient Owner, Guid DefinitionId, Guid AgentId)> SetupDefinitionRequiringSecretParamAsync()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (_, agentId) = await CreateAuthenticatedClientAsync();

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

        await GrantRoleAsync(agentId, Role.Developer, RoleScopeType.Environment, environment!.Id);

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
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        return (owner, definition!.Id, agentId);
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

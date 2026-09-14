using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// M14 (docs/modules/07_LIFECYCLE_ROTATION_REVOCATION.md §102 invariant 2 / UC-02 / AC-02):
// revoking a secret must cascade to its SecretAccessGrant and McpServerAssignment bindings
// and report the counts, but must NOT touch RoleAssignment (scope-wide access to every
// other secret in the same Environment) — that boundary is the whole point of the decision
// recorded in IMPLEMENTATION_READINESS.md, so it gets its own explicit test, not just an
// absence of assertions.
public sealed class SecretRevokeCascadeTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Revoke_CascadesToAccessGrantAndMcpAssignment_ReportsCounts_ButLeavesRoleAssignmentIntact()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (agent, agentId) = await CreateAuthenticatedClientAsync();

        var (organizationId, environmentId) = await CreateOrgProjectEnvironmentAsync(owner, ownerId);
        await GrantRoleAsync(agentId, Role.Developer, RoleScopeType.Environment, environmentId);

        var secretId = await CreateSecretAsync(owner, environmentId, "REVOKE_CASCADE_TEST", "s3cr3t");

        var grantResponse = await owner.PostAsJsonAsync($"/api/v1/secrets/{secretId}/access-grants", new { identityId = agentId });
        Assert.Equal(HttpStatusCode.Created, grantResponse.StatusCode);
        var grant = await grantResponse.Content.ReadFromJsonAsync<GrantDto>();

        var definitionResponse = await owner.PostAsJsonAsync($"/api/v1/organizations/{organizationId}/mcp-servers", new
        {
            name = $"srv-{Guid.NewGuid():N}",
            transport = "Stdio",
            command = "some-mcp-server",
            args = (List<string>?)null,
            url = (string?)null,
            timeout = (int?)null,
            connectTimeout = (int?)null,
            staticEnv = (Dictionary<string, string>?)null,
            secretParamNames = new[] { "TOKEN" },
        });
        var definition = await definitionResponse.Content.ReadFromJsonAsync<IdResponse>();

        var assignResponse = await owner.PostAsJsonAsync($"/api/v1/identities/{agentId}/mcp-assignments", new
        {
            mcpServerDefinitionId = definition!.Id,
            paramValues = new Dictionary<string, object> { ["TOKEN"] = new { secretId } },
        });
        Assert.Equal(HttpStatusCode.Created, assignResponse.StatusCode);
        var assignment = await assignResponse.Content.ReadFromJsonAsync<IdResponse>();

        // Impact analysis, before revoking: all three consumers should be visible.
        var impactResponse = await owner.GetAsync($"/api/v1/secrets/{secretId}/impact");
        Assert.Equal(HttpStatusCode.OK, impactResponse.StatusCode);
        var impact = await impactResponse.Content.ReadFromJsonAsync<ImpactDto>();
        Assert.Contains(impact!.RoleAssignmentConsumers, c => c.IdentityId == agentId);
        Assert.Contains(agentId, impact.AccessGrantIdentityIds);
        Assert.Contains(assignment!.Id, impact.McpAssignmentIds);

        var revokeResponse = await owner.PostAsync($"/api/v1/secrets/{secretId}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<RevokeDto>();
        Assert.Equal("Revoked", revoked!.Status);
        Assert.Equal(1, revoked.RevokedAccessGrants);
        Assert.Equal(1, revoked.RevokedMcpAssignments);

        // Idempotent: revoking again reports zero (nothing left active to cascade to).
        var secondRevoke = await owner.PostAsync($"/api/v1/secrets/{secretId}/revoke", content: null);
        var secondRevoked = await secondRevoke.Content.ReadFromJsonAsync<RevokeDto>();
        Assert.Equal(0, secondRevoked!.RevokedAccessGrants);
        Assert.Equal(0, secondRevoked.RevokedMcpAssignments);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var grantRow = await db.SecretAccessGrants.SingleAsync(g => g.Id == grant!.Id);
        Assert.NotNull(grantRow.RevokedAt);
        var assignmentRow = await db.McpServerAssignments.SingleAsync(a => a.Id == assignment.Id);
        Assert.NotNull(assignmentRow.RevokedAt);

        // The boundary this whole feature is about: the agent's RoleAssignment on the
        // Environment (access to every OTHER secret there) must survive a single secret's
        // revoke untouched.
        var roleRow = await db.RoleAssignments.SingleAsync(r => r.IdentityId == agentId && r.ScopeType == RoleScopeType.Environment && r.ScopeId == environmentId);
        Assert.Null(roleRow.RevokedAt);
    }

    private async Task<(Guid OrganizationId, Guid EnvironmentId)> CreateOrgProjectEnvironmentAsync(HttpClient owner, Guid ownerId)
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

    private async Task<Guid> CreateSecretAsync(HttpClient owner, Guid environmentId, string name, string value)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId,
            name,
            type = "GenericSecret",
            provider = (string?)null,
            description = (string?)null,
            value,
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var secret = await response.Content.ReadFromJsonAsync<IdResponse>();
        return secret!.Id;
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

    private sealed record GrantDto(Guid Id, Guid SecretId, Guid IdentityId, Guid GrantedBy, string Status, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

    private sealed record RevokeDto(
        Guid Id, Guid EnvironmentId, string Name, string Type, string? Provider, string? Description,
        string Status, int CurrentVersion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt,
        int RevokedAccessGrants, int RevokedMcpAssignments);

    private sealed record RoleConsumerDto(Guid IdentityId, string Role);

    private sealed record ImpactDto(Guid SecretId, List<RoleConsumerDto> RoleAssignmentConsumers, List<Guid> AccessGrantIdentityIds, List<Guid> McpAssignmentIds);
}

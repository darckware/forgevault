using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md (SecretAccessGrant decision, this revision).
// RoleAssignment is scope-wide (Organization/Project/Environment): an identity with a role
// there reads every Secret in it. SecretAccessGrant is the narrower alternative — "this
// identity, this one Secret" — for an agent that should get exactly one credential. Tested
// with the same invariants as every other privileged endpoint here: RBAC-gated, idempotent,
// revoke actually removes effective access, and a grant never leaks the value in a denial.
public sealed class SecretAccessGrantEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task GrantedIdentity_CanReadValue_WithoutAnyRoleAssignment_AndGrantIsIdempotent()
    {
        var secretValue = $"sk-grant-{Guid.NewGuid():N}";
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (agent, agentId) = await CreateAuthenticatedClientAsync();

        var environment = await CreateOrgProjectEnvironmentAsync(owner, ownerId);
        var secretId = await CreateSecretAsync(owner, environment, "SITE_LOGIN_PASSWORD", secretValue);

        // No RoleAssignment anywhere for the agent — scope RBAC alone would forbid this.
        var deniedBeforeGrant = await agent.GetAsync($"/api/v1/secrets/{secretId}/value");
        Assert.Equal(HttpStatusCode.Forbidden, deniedBeforeGrant.StatusCode);

        var firstGrant = await owner.PostAsJsonAsync($"/api/v1/secrets/{secretId}/access-grants", new { identityId = agentId });
        Assert.Equal(HttpStatusCode.Created, firstGrant.StatusCode);
        var grant = await firstGrant.Content.ReadFromJsonAsync<GrantDto>();
        Assert.Equal("active", grant!.Status);

        var secondGrant = await owner.PostAsJsonAsync($"/api/v1/secrets/{secretId}/access-grants", new { identityId = agentId });
        Assert.Equal(HttpStatusCode.OK, secondGrant.StatusCode);
        var second = await secondGrant.Content.ReadFromJsonAsync<GrantDto>();
        Assert.Equal(grant.Id, second!.Id);

        var reveal = await agent.GetAsync($"/api/v1/secrets/{secretId}/value");
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
        var envelope = await reveal.Content.ReadFromJsonAsync<CredentialEnvelopeDto>();
        Assert.Equal(secretValue, envelope!.Credentials!["value"]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var count = await db.SecretAccessGrants.CountAsync(g => g.SecretId == secretId && g.IdentityId == agentId && g.RevokedAt == null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Revoke_RemovesAccess_IsIdempotent_AndStrangerCannotGrantOrRevoke()
    {
        var secretValue = $"sk-grant-revoke-{Guid.NewGuid():N}";
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (agent, agentId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();

        var environment = await CreateOrgProjectEnvironmentAsync(owner, ownerId);
        var secretId = await CreateSecretAsync(owner, environment, "DB_CREDENTIAL", secretValue);

        // A stranger with no SecretWrite at this scope cannot grant access to anyone.
        var deniedGrant = await stranger.PostAsJsonAsync($"/api/v1/secrets/{secretId}/access-grants", new { identityId = agentId });
        Assert.Equal(HttpStatusCode.Forbidden, deniedGrant.StatusCode);

        var grantResponse = await owner.PostAsJsonAsync($"/api/v1/secrets/{secretId}/access-grants", new { identityId = agentId });
        var grant = await grantResponse.Content.ReadFromJsonAsync<GrantDto>();

        var strangerRevoke = await stranger.PostAsync($"/api/v1/secret-access-grants/{grant!.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, strangerRevoke.StatusCode);

        var revoke = await owner.PostAsync($"/api/v1/secret-access-grants/{grant.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = await revoke.Content.ReadFromJsonAsync<GrantDto>();
        Assert.Equal("revoked", revoked!.Status);

        // Idempotent: revoking again is a no-op, still 200.
        var secondRevoke = await owner.PostAsync($"/api/v1/secret-access-grants/{grant.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, secondRevoke.StatusCode);

        var deniedAfterRevoke = await agent.GetAsync($"/api/v1/secrets/{secretId}/value");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAfterRevoke.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var deniedLog = await db.AuditLogs.SingleAsync(a =>
            a.ResourceId == secretId && a.Action == "FAILED_ACCESS" && a.ResourceType == "secret" && a.ActorId == agentId.ToString());
        Assert.DoesNotContain(secretValue, deniedLog.Metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScopeRoleAlone_IsStillSufficient_GrantIsAdditiveNotReplacing()
    {
        // A grant is an additional path to access, never a narrower replacement of RBAC —
        // an identity with a real scope role keeps working with zero grants.
        var secretValue = $"sk-scope-{Guid.NewGuid():N}";
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (developer, developerId) = await CreateAuthenticatedClientAsync();

        var environment = await CreateOrgProjectEnvironmentAsync(owner, ownerId);
        await GrantRoleAsync(developerId, Role.Developer, RoleScopeType.Environment, environment);
        var secretId = await CreateSecretAsync(owner, environment, "PROVIDER_TOKEN", secretValue);

        var reveal = await developer.GetAsync($"/api/v1/secrets/{secretId}/value");
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
    }

    private async Task<Guid> CreateOrgProjectEnvironmentAsync(HttpClient owner, Guid ownerId)
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

        return environment!.Id;
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

    private sealed record CredentialEnvelopeDto(
        string RequestId, string Identity, string Resource, string AccessMode,
        DateTimeOffset? ExpiresAt, Dictionary<string, string>? Credentials);
}

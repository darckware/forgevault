using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// M9 (docs/modules/04_AUTHORIZATION_AND_POLICY.md §7, assignRole/revokeRoleAssignment).
// Before this, the only way to grant a RoleAssignment was a direct INSERT against the
// database — this is the endpoint closing that gap. Same RBAC/idempotency/no-escalation
// invariants tested for every other privileged endpoint in this project.
public sealed class RoleAssignmentEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Owner_CanGrantRole_StrangerCannot_AndGrantIsIdempotent()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (_, granteeId) = await CreateAuthenticatedClientAsync();

        var organization = await CreateOrganizationAsync(owner);
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization);

        var deniedResponse = await stranger.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var firstGrant = await owner.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });
        Assert.Equal(HttpStatusCode.Created, firstGrant.StatusCode);
        var first = await firstGrant.Content.ReadFromJsonAsync<RoleAssignmentDto>();
        Assert.Equal("active", first!.Status);

        // Idempotent: granting the exact same (identity, role, scope) again returns the
        // existing row, not a duplicate.
        var secondGrant = await owner.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });
        Assert.Equal(HttpStatusCode.OK, secondGrant.StatusCode);
        var second = await secondGrant.Content.ReadFromJsonAsync<RoleAssignmentDto>();
        Assert.Equal(first.Id, second!.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var count = await db.RoleAssignments.CountAsync(r => r.IdentityId == granteeId && r.RevokedAt == null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GrantingIntoNonexistentScope_Is404()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var organization = await CreateOrganizationAsync(owner);
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization);

        var response = await owner.PostAsJsonAsync($"/api/v1/identities/{Guid.NewGuid()}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeveloperRole_CannotGrantRoles_EvenAtItsOwnOrganization()
    {
        // A role that has SecretWrite/SecretReadValue but not RoleAssignmentWrite must not
        // be able to grant roles, even at a scope where it otherwise has real permissions —
        // proves RoleAssignmentWrite is a distinct permission, not implied by "some access".
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (developer, developerId) = await CreateAuthenticatedClientAsync();
        var (_, granteeId) = await CreateAuthenticatedClientAsync();

        var organization = await CreateOrganizationAsync(owner);
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization);
        await GrantRoleAsync(developerId, Role.Developer, RoleScopeType.Organization, organization);

        var response = await developer.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_RemovesEffectivePermission_IsIdempotent_AndRequiresRoleAssignmentWrite()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (grantee, granteeId) = await CreateAuthenticatedClientAsync();

        var organization = await CreateOrganizationAsync(owner);
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization);

        var grantResponse = await owner.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });
        var grant = await grantResponse.Content.ReadFromJsonAsync<RoleAssignmentDto>();

        var projectResponse = await owner.PostAsJsonAsync(
            $"/api/v1/organizations/{organization}/projects", new { name = "proj", slug = "proj" });
        var project = await projectResponse.Content.ReadFromJsonAsync<IdResponse>();
        var environmentResponse = await owner.PostAsJsonAsync(
            $"/api/v1/projects/{project!.Id}/environments", new { name = "Production", slug = "production" });
        var environment = await environmentResponse.Content.ReadFromJsonAsync<IdResponse>();

        var createBeforeRevoke = await grantee.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment!.Id,
            name = "BEFORE_REVOKE",
            type = "GenericSecret",
            provider = (string?)null,
            description = (string?)null,
            value = "irrelevant",
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.Created, createBeforeRevoke.StatusCode);

        var deniedRevoke = await stranger.PostAsync($"/api/v1/role-assignments/{grant!.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRevoke.StatusCode);

        var revokeResponse = await owner.PostAsync($"/api/v1/role-assignments/{grant.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<RoleAssignmentDto>();
        Assert.Equal("revoked", revoked!.Status);

        // Idempotent.
        var secondRevoke = await owner.PostAsync($"/api/v1/role-assignments/{grant.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, secondRevoke.StatusCode);

        var createAfterRevoke = await grantee.PostAsJsonAsync("/api/v1/secrets", new
        {
            environmentId = environment.Id,
            name = "AFTER_REVOKE",
            type = "GenericSecret",
            provider = (string?)null,
            description = (string?)null,
            value = "irrelevant",
            expiresAt = (DateTimeOffset?)null,
        });
        Assert.Equal(HttpStatusCode.Forbidden, createAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task ListingAnIdentitysRoles_RequiresRoleAssignmentWrite()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (_, granteeId) = await CreateAuthenticatedClientAsync();

        var organization = await CreateOrganizationAsync(owner);
        await GrantRoleAsync(ownerId, Role.Owner, RoleScopeType.Organization, organization);
        await owner.PostAsJsonAsync($"/api/v1/identities/{granteeId}/role-assignments", new
        {
            role = "Developer",
            scopeType = "Organization",
            scopeId = organization,
        });

        var deniedResponse = await stranger.GetAsync($"/api/v1/identities/{granteeId}/role-assignments");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var allowedResponse = await owner.GetAsync($"/api/v1/identities/{granteeId}/role-assignments");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var list = await allowedResponse.Content.ReadFromJsonAsync<List<RoleAssignmentDto>>();
        Assert.Single(list!);
        Assert.Equal("Developer", list![0].Role);
    }

    private async Task<Guid> CreateOrganizationAsync(HttpClient client)
    {
        var orgResponse = await client.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        return organization!.Id;
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

    private sealed record RoleAssignmentDto(Guid Id, Guid IdentityId, string Role, string ScopeType, Guid ScopeId, string Status);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// M13 — before UserEndpoints existed, the only way to create a human User account was a
// direct INSERT against the database (every test file's CreateAuthenticatedClientAsync
// helper does exactly that). Same invariants as every other privileged endpoint here:
// RBAC-gated by UserManage (Owner/Admin only), and a deactivated account must actually stop
// being able to log in, not just flip a flag nobody reads.
public sealed class UserEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task Owner_CanCreateUser_StrangerCannot_AndDuplicateEmailIs409()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var email = $"new-{Guid.NewGuid():N}@example.test";

        var deniedResponse = await stranger.PostAsJsonAsync("/api/v1/users", new { email, password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var createResponse = await owner.PostAsJsonAsync("/api/v1/users", new { email, password = "correct horse battery staple" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.True(created!.IsActive);

        var duplicateResponse = await owner.PostAsJsonAsync("/api/v1/users", new { email, password = "another password entirely" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        var shortPasswordResponse = await owner.PostAsJsonAsync(
            "/api/v1/users", new { email = $"short-{Guid.NewGuid():N}@example.test", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, shortPasswordResponse.StatusCode);
    }

    [Fact]
    public async Task NewUser_CanLogin_ThenDeactivate_BlocksLogin_ThenReactivate_RestoresIt()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var email = $"lifecycle-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";
        var createResponse = await owner.PostAsJsonAsync("/api/v1/users", new { email, password });
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();

        var client = factory.CreateClient();
        var firstLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, mfaCode = (string?)null });
        Assert.Equal(HttpStatusCode.OK, firstLogin.StatusCode);

        var deactivateResponse = await owner.PostAsync($"/api/v1/users/{created!.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);
        var deactivated = await deactivateResponse.Content.ReadFromJsonAsync<UserDto>();
        Assert.False(deactivated!.IsActive);

        var blockedLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, mfaCode = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, blockedLogin.StatusCode);
        var blockedBody = await blockedLogin.Content.ReadAsStringAsync();
        Assert.Contains("account_disabled", blockedBody, StringComparison.Ordinal);

        var reactivateResponse = await owner.PostAsync($"/api/v1/users/{created.Id}/reactivate", content: null);
        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

        var restoredLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, mfaCode = (string?)null });
        Assert.Equal(HttpStatusCode.OK, restoredLogin.StatusCode);
    }

    [Fact]
    public async Task Owner_CannotDeactivateSelf()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var response = await owner.PostAsync($"/api/v1/users/{ownerId}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var stillActive = await db.Users.Where(u => u.Id == ownerId).Select(u => u.IsActive).SingleAsync();
        Assert.True(stillActive);
    }

    [Fact]
    public async Task Delete_RemovesUserAndRevokesRoleAssignments_CannotDeleteSelf_RequiresUserManage()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (_, targetId) = await CreateAuthenticatedClientAsync();
        var organizationId = await GrantOwnerAtNewOrganizationAsync(owner, ownerId);
        await GrantRoleAsync(targetId, Role.Developer, organizationId);

        var selfDeleteResponse = await owner.DeleteAsync($"/api/v1/users/{ownerId}");
        Assert.Equal(HttpStatusCode.Conflict, selfDeleteResponse.StatusCode);

        var deniedResponse = await stranger.DeleteAsync($"/api/v1/users/{targetId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var deleteResponse = await owner.DeleteAsync($"/api/v1/users/{targetId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == targetId));
        var assignment = await db.RoleAssignments.SingleAsync(r => r.IdentityId == targetId);
        Assert.NotNull(assignment.RevokedAt);
    }

    [Fact]
    public async Task List_RequiresUserManage()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var deniedResponse = await stranger.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var allowedResponse = await owner.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    private async Task<Guid> GrantOwnerAtNewOrganizationAsync(HttpClient owner, Guid ownerId)
    {
        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();
        await GrantRoleAsync(ownerId, Role.Owner, organization!.Id);
        return organization.Id;
    }

    private async Task GrantRoleAsync(Guid identityId, Role role, Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = identityId,
            Role = role,
            ScopeType = RoleScopeType.Organization,
            ScopeId = organizationId,
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

    private sealed record UserDto(Guid Id, string Email, bool MfaEnabled, bool IsActive, DateTimeOffset CreatedAt);
}

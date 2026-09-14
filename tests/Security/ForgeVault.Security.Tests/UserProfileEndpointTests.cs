using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// M15 — profile fields (Username/FirstName/LastName/AvatarDataUrl/IsAdmin), mirroring
// ForgeHub's User model. IsAdmin here is cosmetic only (explicit decision this revision,
// unlike ForgeHub's real bypass flag) — this file doesn't assert anything about permission
// bypass because there is deliberately none to test.
public sealed class UserProfileEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task SelfUpdate_ChangesNameAndAvatar_ButRejectsOversizedOrNonImageAvatar()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        var tooLarge = new string('a', 2_000_001);
        var oversizedResponse = await client.PutAsJsonAsync("/api/v1/auth/me", new { firstName = (string?)null, lastName = (string?)null, avatarDataUrl = tooLarge });
        Assert.Equal(HttpStatusCode.BadRequest, oversizedResponse.StatusCode);

        var notImageResponse = await client.PutAsJsonAsync("/api/v1/auth/me", new { firstName = (string?)null, lastName = (string?)null, avatarDataUrl = "not-a-data-url" });
        Assert.Equal(HttpStatusCode.BadRequest, notImageResponse.StatusCode);

        var validAvatar = "data:image/png;base64,iVBORw0KGgo=";
        var updateResponse = await client.PutAsJsonAsync("/api/v1/auth/me", new { firstName = "Ada", lastName = "Lovelace", avatarDataUrl = validAvatar });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("Ada", updated!.FirstName);
        Assert.Equal("Lovelace", updated.LastName);
        Assert.Equal(validAvatar, updated.AvatarDataUrl);

        var meResponse = await client.GetAsync("/api/v1/auth/me");
        var me = await meResponse.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal("Ada", me!.FirstName);
    }

    [Fact]
    public async Task AdminPatch_SetsUsernameAndAdminBadge_RejectsDuplicateUsername_RequiresUserManage()
    {
        var (owner, ownerId) = await CreateAuthenticatedClientAsync();
        var (stranger, _) = await CreateAuthenticatedClientAsync();
        var (_, targetId) = await CreateAuthenticatedClientAsync();
        await GrantOwnerAtNewOrganizationAsync(owner, ownerId);

        var username = $"ada-{Guid.NewGuid():N}";

        var deniedResponse = await stranger.PatchAsJsonAsync($"/api/v1/users/{targetId}", new { username = "someone" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var patchResponse = await owner.PatchAsJsonAsync($"/api/v1/users/{targetId}", new { username, isAdmin = true });
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await patchResponse.Content.ReadFromJsonAsync<MeDto>();
        Assert.Equal(username, patched!.Username);
        Assert.True(patched.IsAdmin);

        var (_, secondTargetId) = await CreateAuthenticatedClientAsync();
        var duplicateResponse = await owner.PatchAsJsonAsync($"/api/v1/users/{secondTargetId}", new { username });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var row = await db.Users.SingleAsync(u => u.Id == targetId);
        Assert.True(row.IsAdmin);
        Assert.Equal(username, row.Username);
    }

    private async Task GrantOwnerAtNewOrganizationAsync(HttpClient owner, Guid ownerId)
    {
        var orgResponse = await owner.PostAsJsonAsync("/api/v1/organizations", new
        {
            name = $"acme-{Guid.NewGuid():N}",
            slug = $"acme-{Guid.NewGuid():N}",
        });
        var organization = await orgResponse.Content.ReadFromJsonAsync<IdResponse>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        db.RoleAssignments.Add(new RoleAssignment
        {
            Id = Guid.NewGuid(),
            IdentityId = ownerId,
            Role = Role.Owner,
            ScopeType = RoleScopeType.Organization,
            ScopeId = organization!.Id,
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

    private sealed record MeDto(
        string Id, string Email, bool MfaEnabled,
        string? Username, string? FirstName, string? LastName, string? AvatarDataUrl, bool IsAdmin);
}

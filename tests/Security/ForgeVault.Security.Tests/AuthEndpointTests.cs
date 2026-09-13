using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.Security.Tests;

// New in this revision: the account-profile UI added a self-service "change password" flow,
// which required a REST endpoint (POST /api/v1/auth/change-password) that didn't exist
// before — the only prior way to change a user's password was a direct database write.
public sealed class AuthEndpointTests(LoggingWebApplicationFactory factory) : IClassFixture<LoggingWebApplicationFactory>
{
    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Is401_AndOldPasswordStillWorks()
    {
        var (client, email) = await CreateAuthenticatedClientAsync("CorrectHorseBatteryStaple1!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = "definitely-not-the-current-password",
            newPassword = "SomethingElseEntirely2!",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The old password must still work — a failed change must not have side effects.
        var relogin = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = "CorrectHorseBatteryStaple1!" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_TooShort_Is400()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("CorrectHorseBatteryStaple1!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = "CorrectHorseBatteryStaple1!",
            newPassword = "short",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Success_OldPasswordStopsWorking_AndRevokesOtherSessions()
    {
        const string oldPassword = "CorrectHorseBatteryStaple1!";
        const string newPassword = "NewCorrectHorseBatteryStaple2!";

        var (firstSessionClient, email) = await CreateAuthenticatedClientAsync(oldPassword);

        // A second, independent login for the same account — simulates another device/tab
        // that should be forced to re-authenticate once the password changes.
        var secondLoginResponse = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = oldPassword });
        var secondLogin = await secondLoginResponse.Content.ReadFromJsonAsync<LoginResponseDto>();

        var changeResponse = await firstSessionClient.PostAsJsonAsync("/api/v1/auth/change-password", new
        {
            currentPassword = oldPassword,
            newPassword,
        });
        Assert.Equal(HttpStatusCode.OK, changeResponse.StatusCode);

        // Old password is rejected everywhere now.
        var oldPasswordLogin = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = oldPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        // New password works.
        var newPasswordLogin = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);

        // The second session's refresh token (issued before the change) must have been
        // revoked, not just the caller's own — a password change is meant to end every
        // other session, same principle as the reuse-detection path.
        var secondRefresh = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = secondLogin!.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);
    }

    [Fact]
    public async Task Me_ReflectsMfaEnabled()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("CorrectHorseBatteryStaple1!");

        var before = await client.GetFromJsonAsync<MeDto>("/api/v1/auth/me");
        Assert.False(before!.MfaEnabled);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == Guid.Parse(before.Id));
            user.MfaEnabled = true;
            await db.SaveChangesAsync();
        }

        // mfa_enabled is read from the access token's own claim, not a fresh DB lookup, so a
        // flip on the row alone shouldn't retroactively change what an already-issued token
        // reports — confirms the claim is what /me actually reads, not the row.
        var stillOld = await client.GetFromJsonAsync<MeDto>("/api/v1/auth/me");
        Assert.False(stillOld!.MfaEnabled);
    }

    private async Task<(HttpClient Client, string Email)> CreateAuthenticatedClientAsync(string password)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
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
        return (client, email);
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record MeDto(string Id, string Email, bool MfaEnabled);
}

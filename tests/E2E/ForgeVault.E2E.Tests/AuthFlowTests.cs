using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.E2E.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M3 "done when": an E2E test
// logs in, receives a JWT, calls an authenticated placeholder endpoint, and gets 401
// without a token. Also exercises the refresh-token rotation + reuse-detection invariant
// from docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4.
public sealed class AuthFlowTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Login_ThenAuthenticatedCall_Succeeds_AndFailsWithoutAToken()
    {
        var (email, password) = await SeedUserAsync();
        var client = factory.CreateClient();

        var unauthorized = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var login = await LoginAsync(client, email, password);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var meBody = await me.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(email, meBody!.Email);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var (email, _) = await SeedUserAsync();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "definitely-wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTheToken_AndRejectsReuseOfTheOldOne()
    {
        var (email, password) = await SeedUserAsync();
        var client = factory.CreateClient();
        var login = await LoginAsync(client, email, password);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var rotated = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotEqual(login.RefreshToken, rotated!.RefreshToken);

        // Reusing the already-rotated token must fail (theft/reuse detection).
        var reuseResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // And it must have revoked the whole family — the freshly rotated token is now unusable too.
        var afterReuseResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuseResponse.StatusCode);
    }

    private async Task<(string Email, string Password)> SeedUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(password),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        return (email, password);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        return body;
    }

    private sealed record LoginResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record MeResponse(string Id, string Email);
}

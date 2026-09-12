using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ForgeVault.Application.Auth;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeVault.E2E.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md, milestone M6 "done when": TOTP enrollment
// + login-with-MFA works end to end.
public sealed class MfaFlowTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task EnrollVerifyAndLoginWithMfa_Succeeds_AndBlocksLoginWithoutOrWithWrongCode()
    {
        var (client, userId, email, password) = await CreateAuthenticatedUserAsync();

        var enrollResponse = await client.PostAsync("/api/v1/auth/mfa/enroll", content: null);
        Assert.Equal(HttpStatusCode.OK, enrollResponse.StatusCode);
        var enrollment = await enrollResponse.Content.ReadFromJsonAsync<MfaEnrollResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(enrollment!.Base32Secret));
        Assert.StartsWith("otpauth://totp/", enrollment.OtpAuthUri, StringComparison.Ordinal);

        var wrongVerify = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongVerify.StatusCode);

        var validCode = await ComputeCurrentTotpCodeAsync(userId);
        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { code = validCode });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        // MFA is now mandatory for this account.
        var loginWithoutCode = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithoutCode.StatusCode);
        var withoutCodeError = await loginWithoutCode.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.Equal("mfa_required", withoutCodeError!.Error);

        var loginWithWrongCode = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { email, password, mfaCode = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithWrongCode.StatusCode);

        var freshCode = await ComputeCurrentTotpCodeAsync(userId);
        var loginWithMfa = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { email, password, mfaCode = freshCode });
        Assert.Equal(HttpStatusCode.OK, loginWithMfa.StatusCode);
    }

    private async Task<string> ComputeCurrentTotpCodeAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ForgeVaultDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<IEnvelopeEncryptionService>();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        var payload = new EncryptedPayload(
            user.MfaSecretCiphertext!, user.MfaSecretEncryptedDek!, user.MfaSecretNonce!, user.MfaSecretAuthTag!, user.MfaSecretAlgorithm!);
        var secret = await crypto.DecryptAsync(payload, CancellationToken.None);

        return totp.ComputeCode(secret, DateTimeOffset.UtcNow);
    }

    private async Task<(HttpClient Client, Guid UserId, string Email, string Password)> CreateAuthenticatedUserAsync()
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
        return (client, userId, email, password);
    }

    private sealed record LoginResponseDto(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed record ErrorDto(string Error);

    private sealed record MfaEnrollResponseDto(string Base32Secret, string OtpAuthUri);
}

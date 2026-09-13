using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ForgeVault.Application.Auth;
using ForgeVault.Application.Security;
using ForgeVault.Domain.Entities;
using ForgeVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ForgeVault.Infrastructure.Auth;

// docs/modules/02_IDENTITY_AND_AUTHENTICATION.md — login/refresh (M3) plus TOTP MFA (M6,
// docs/architecture/IMPLEMENTATION_READINESS.md §4).
public sealed class AuthService(
    ForgeVaultDbContext db,
    IPasswordHasher passwordHasher,
    IEnvelopeEncryptionService crypto,
    ITotpService totp,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<AuthOutcome> LoginAsync(string email, string password, string? mfaCode, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Same failure reason regardless of whether the email exists or the password is
        // wrong — never let a caller distinguish "no such user" from "wrong password".
        if (user is null || !passwordHasher.Verify(password, user.PasswordHash))
        {
            logger.LogInformation("Login failed: invalid credentials for the attempted account.");
            return new AuthFailure("invalid_credentials");
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(mfaCode))
            {
                logger.LogInformation("Login rejected: account requires MFA but no code was presented.");
                return new AuthFailure("mfa_required");
            }

            var payload = MfaSecretCodec.TryGetPayload(user);
            if (payload is null)
            {
                // MfaEnabled=true implies a secret must exist — an inconsistent row, not a
                // caller error. Fail closed rather than silently accepting no MFA.
                logger.LogWarning("Login rejected: account has MfaEnabled but no MFA secret on record.");
                return new AuthFailure("mfa_required");
            }

            var secret = await crypto.DecryptAsync(payload, ct);
            var codeIsValid = totp.ValidateCode(secret, mfaCode, DateTimeOffset.UtcNow);
            CryptographicOperations.ZeroMemory(secret);

            if (!codeIsValid)
            {
                logger.LogInformation("Login rejected: invalid MFA code.");
                return new AuthFailure("invalid_mfa_code");
            }
        }

        return await IssueTokensAsync(user, familyId: Guid.NewGuid(), mfaVerified: user.MfaEnabled, ct);
    }

    public async Task<AuthOutcome> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var tokenHash = HashToken(refreshToken);
        var existing = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (existing is null)
        {
            logger.LogInformation("Refresh failed: presented token not recognized.");
            return new AuthFailure("invalid_refresh_token");
        }

        if (existing.RevokedAt is not null)
        {
            // A token that was already rotated is being presented again — treat the
            // entire rotation family as compromised (docs/ForgeVault.md §53).
            await RevokeFamilyAsync(existing.FamilyId, ct);
            logger.LogWarning("Refresh token reuse detected for a token family; the family was revoked.");
            return new AuthFailure("refresh_token_reused");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Refresh failed: token expired.");
            return new AuthFailure("refresh_token_expired");
        }

        existing.RevokedAt = DateTimeOffset.UtcNow;

        // Carries the original login's MFA-verified status forward — a caller who proved
        // possession of their authenticator once isn't asked again every 15 minutes.
        return await IssueTokensAsync(existing.User!, existing.FamilyId, existing.MfaVerified, ct);
    }

    public async Task<MfaEnrollment> EnrollMfaAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);

        var secret = totp.GenerateSecret();
        var payload = await crypto.EncryptAsync(secret, ct);
        MfaSecretCodec.ApplyTo(user, payload);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        var base32Secret = Base32.Encode(secret);
        CryptographicOperations.ZeroMemory(secret);

        var otpAuthUri = $"otpauth://totp/ForgeVault:{Uri.EscapeDataString(user.Email)}?secret={base32Secret}&issuer=ForgeVault";
        return new MfaEnrollment(base32Secret, otpAuthUri);
    }

    public async Task<bool> VerifyMfaAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        var payload = MfaSecretCodec.TryGetPayload(user);
        if (payload is null)
        {
            logger.LogInformation("MFA verify rejected: no enrollment in progress for this account.");
            return false;
        }

        var secret = await crypto.DecryptAsync(payload, ct);
        var isValid = totp.ValidateCode(secret, code, DateTimeOffset.UtcNow);
        CryptographicOperations.ZeroMemory(secret);

        if (!isValid)
        {
            logger.LogInformation("MFA verify rejected: invalid code.");
            return false;
        }

        user.MfaEnabled = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        if (!passwordHasher.Verify(currentPassword, user.PasswordHash))
        {
            logger.LogInformation("Change password rejected: current password did not match.");
            return false;
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // Every other session (every other refresh token family) dies with the old
        // password — the caller's own current access token still works until it expires
        // (max 15 min), same as any other refresh-token revocation in this file.
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<AuthOutcome> IssueTokensAsync(User user, Guid familyId, bool mfaVerified, CancellationToken ct)
    {
        var options = jwtOptions.Value;
        var now = DateTimeOffset.UtcNow;
        var accessTokenExpiresAt = now.AddMinutes(options.AccessTokenLifetimeMinutes);

        var accessToken = CreateAccessToken(user, mfaVerified, now, accessTokenExpiresAt, options);
        var rawRefreshToken = GenerateRawToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawRefreshToken),
            FamilyId = familyId,
            IssuedAt = now,
            ExpiresAt = now.AddDays(options.RefreshTokenLifetimeDays),
            MfaVerified = mfaVerified,
        });

        await db.SaveChangesAsync(ct);

        return new AuthSuccess(accessToken, rawRefreshToken, accessTokenExpiresAt);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static string CreateAccessToken(
        User user, bool mfaVerified, DateTimeOffset issuedAt, DateTimeOffset expiresAt, JwtOptions options)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // mfa_enabled/mfa_verified back ForgeVault.Api.Authorization.MfaAuthorizationHandler:
        // MFA is only ever required of accounts that actually enrolled (mfa_enabled), and
        // even then only once this specific session proved a code (mfa_verified).
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("mfa_enabled", user.MfaEnabled ? "true" : "false"),
            new("mfa_verified", mfaVerified ? "true" : "false"),
        ];

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Base64url, matching common bearer/refresh-token conventions (no padding, URL-safe).
    private static string GenerateRawToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashToken(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}

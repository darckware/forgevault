namespace ForgeVault.Application.Auth;

// docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §5 (Login, RefreshSession, MFA commands).
public interface IAuthService
{
    // mfaCode is required and validated only when the account has MfaEnabled=true
    // (docs/architecture/IMPLEMENTATION_READINESS.md §4 / M6) — accounts that never
    // enrolled are unaffected. emailOrUsername accepts either identifier (M17) — a User's
    // Username (M15) is optional, so email always works even for accounts with none set.
    Task<AuthOutcome> LoginAsync(string emailOrUsername, string password, string? mfaCode, CancellationToken ct);

    Task<AuthOutcome> RefreshAsync(string refreshToken, CancellationToken ct);

    // Generates and stores (envelope-encrypted) a new TOTP secret. Does NOT enable MFA by
    // itself — enrollment only takes effect after VerifyMfaAsync succeeds
    // (docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §5 UC-05).
    Task<MfaEnrollment> EnrollMfaAsync(Guid userId, CancellationToken ct);

    // Confirms enrollment with a real code from the authenticator app; on success flips
    // MfaEnabled to true. Returns false (not an exception) on an invalid code — an
    // expected, non-exceptional outcome, same pattern as AuthOutcome for login.
    Task<bool> VerifyMfaAsync(Guid userId, string code, CancellationToken ct);

    // Returns false (not an exception) when currentPassword doesn't match — same
    // non-exceptional-failure pattern as VerifyMfaAsync. On success, every other active
    // refresh token for this user is revoked (docs/ForgeVault.md §53 spirit: a password
    // change is exactly the moment every other session should be forced to re-authenticate,
    // same as the reuse-detection path already does for a single compromised family).
    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);

    // Requires the current password for the same reason ChangePasswordAsync does — MFA is a
    // security control, so turning it off must not be possible with only a stolen/still-open
    // session; something the attacker doesn't have (the password) is required too. Returns
    // false (not an exception) when currentPassword doesn't match. Idempotent: disabling an
    // already-disabled account still succeeds and clears any stray secret columns.
    Task<bool> DisableMfaAsync(Guid userId, string currentPassword, CancellationToken ct);
}

public abstract record AuthOutcome;

public sealed record AuthSuccess(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt) : AuthOutcome;

// Reason is a stable machine-readable code (e.g. "invalid_credentials",
// "refresh_token_reused"), never a message that could leak which part of the
// credential pair was wrong (docs/ForgeVault.md §21 spirit applied to auth failures).
public sealed record AuthFailure(string Reason) : AuthOutcome;

// The base32 secret and otpauth:// URI are returned to the caller exactly once, at
// enrollment time — the one legitimate moment a TOTP secret is shown in plaintext, the
// same way a Master Key or a raw agent token is shown once at creation (docs/ForgeVault.md §115).
public sealed record MfaEnrollment(string Base32Secret, string OtpAuthUri);

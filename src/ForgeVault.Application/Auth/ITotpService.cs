namespace ForgeVault.Application.Auth;

// RFC 6238 TOTP (30-second step, 6 digits, HMAC-SHA1 — the algorithm every mainstream
// authenticator app assumes). No third-party dependency: the algorithm is ~30 lines over
// the BCL's HMACSHA1, same rationale as Pbkdf2PasswordHasher not pulling in BCrypt/Argon2.
public interface ITotpService
{
    byte[] GenerateSecret();

    string ComputeCode(byte[] secret, DateTimeOffset timestamp);

    // Accepts a ±1 step (30s) window to tolerate clock drift between server and authenticator.
    bool ValidateCode(byte[] secret, string code, DateTimeOffset timestamp);
}

using System.Security.Cryptography;
using ForgeVault.Application.Auth;

namespace ForgeVault.Infrastructure.Auth;

// PBKDF2-HMACSHA256, built into .NET (no third-party BCrypt/Argon2 dependency needed for
// a capability the BCL already provides correctly). Iteration count follows OWASP's 2023
// Password Storage Cheat Sheet minimum for PBKDF2-SHA256. Encoded as
// "pbkdf2-sha256$<iterations>$<saltBase64>$<hashBase64>" so the iteration count can be
// raised later without breaking verification of hashes created under the old count.
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 600_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt, expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        // Constant-time comparison — a timing difference here would leak how many
        // leading bytes of the hash matched.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

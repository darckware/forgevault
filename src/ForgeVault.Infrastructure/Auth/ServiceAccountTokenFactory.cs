using System.Security.Cryptography;
using System.Text;

namespace ForgeVault.Infrastructure.Auth;

// docs/ForgeVault.md §32, §76, §117. Shared by the token-issuing endpoint and
// ServiceAccountAuthenticationHandler so hashing can never drift between the two.
public static class ServiceAccountTokenFactory
{
    private const string Prefix = "fv_sa_";

    // Base64url, matching RefreshToken's raw-token convention (AuthService.GenerateRawToken).
    public static string GenerateRawToken()
    {
        var random = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"{Prefix}{random}";
    }

    public static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    // e.g. "fv_sa_aGVsbG8...****" — safe to display without revealing the token
    // (docs/ForgeVault.md §117).
    public static string ToDisplayPrefix(string rawToken)
    {
        const int visibleChars = 12;
        var visible = rawToken.Length <= visibleChars ? rawToken : rawToken[..visibleChars];
        return $"{visible}****";
    }
}

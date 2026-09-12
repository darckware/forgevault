using System.ComponentModel.DataAnnotations;

namespace ForgeVault.Infrastructure.Auth;

// docs/ForgeVault.md §45: JWT 5-15 min, refresh token 7-30 days. Validated eagerly at
// startup (ValidateOnStart, wired in ForgeVault.Api's Program.cs) — the process must not
// serve traffic with a missing/too-short signing key.
public sealed class JwtOptions
{
    [Required, MinLength(44)] // 44 base64 chars = 32 raw bytes = 256-bit minimum for HS256
    public string SigningKey { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "forgevault";

    [Required]
    public string Audience { get; set; } = "forgevault-api";

    [Range(1, 60)]
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    [Range(1, 30)]
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}

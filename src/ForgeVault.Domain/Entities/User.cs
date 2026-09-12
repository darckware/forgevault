namespace ForgeVault.Domain.Entities;

// docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4.
public sealed class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }

    // Never the raw password — see ForgeVault.Infrastructure.Auth.Pbkdf2PasswordHasher.
    public required string PasswordHash { get; set; }

    // TOTP enrollment (M6, docs/architecture/IMPLEMENTATION_READINESS.md §4). MfaEnabled
    // only flips to true after a successful /auth/mfa/verify call — enrolling alone does
    // not activate enforcement (docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §5 UC-05).
    public bool MfaEnabled { get; set; }

    // The TOTP secret, envelope-encrypted with the same crypto module as Secret values
    // (docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4: "criptografado como um secret,
    // reusa módulo 03") — four separate columns, mirroring SecretVersion, rather than one
    // packed blob, so the wire format never has to be reinvented ad hoc.
    public byte[]? MfaSecretCiphertext { get; set; }
    public byte[]? MfaSecretEncryptedDek { get; set; }
    public byte[]? MfaSecretNonce { get; set; }
    public byte[]? MfaSecretAuthTag { get; set; }
    public string? MfaSecretAlgorithm { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

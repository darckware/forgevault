namespace ForgeVault.Domain.Entities;

// docs/modules/02_IDENTITY_AND_AUTHENTICATION.md §4; docs/ForgeVault.md §45 (hash-only
// storage, rotation, revocation). FamilyId links every token produced by rotating the
// same original login — reusing an already-rotated (revoked) token in that family is
// treated as theft and revokes the whole family (docs/ForgeVault.md §53).
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    // Whether the login that started this token's family presented a valid TOTP code
    // (M6). Carried forward unchanged on every rotation so a caller who authenticated
    // with MFA isn't asked to re-verify every time their short-lived access token expires.
    public bool MfaVerified { get; set; }

    public User? User { get; set; }
}

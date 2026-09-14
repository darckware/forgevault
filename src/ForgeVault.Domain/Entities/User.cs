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

    // Same convention as ServiceAccount.IsActive — deactivating a User must block login
    // immediately (AuthService.LoginAsync) without deleting the row or its audit trail.
    public bool IsActive { get; set; } = true;

    // M15 — profile fields, mirroring ForgeHub's User model (backend/app/db/models/user.py)
    // field-for-field except FirstName/LastName (ForgeHub has one FullName; split here per
    // explicit request). Username is nullable, not unique at the DB constraint level for
    // every row — legacy/test-seeded users have none — but enforced unique in application
    // code (UserEndpoints) whenever a non-null value is set, backed by a filtered unique
    // index (UserConfiguration) that only constrains non-null values.
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // Self-uploaded profile photo as a "data:image/...;base64,..." URI stored directly in
    // the row — same approach as ForgeHub (avatar_data_url), chosen for the same reason:
    // no static-file/object storage exists for user content, and avatars are small enough
    // that this is simpler than standing one up just for this.
    public string? AvatarDataUrl { get; set; }

    // Cosmetic only (explicit decision this revision) — unlike ForgeHub's is_admin, this
    // flag NEVER bypasses a Permission check. It only affects UI (an "Admin" badge, which
    // nav items render) — every actual authorization decision still goes through
    // RoleAssignment/IPermissionChecker, same as any other identity. Keeping a real global
    // superuser bypass out of a secrets manager was a deliberate call, not an oversight.
    public bool IsAdmin { get; set; }

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

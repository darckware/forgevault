namespace ForgeVault.Domain.Entities;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md §4 — column names follow the DDL in
// docs/ForgeVault.md §83 (ciphertext/encrypted_dek/nonce/auth_tag/algorithm), preferred
// over the older naming in §11. Rows are append-only: never updated after insert
// (docs/ForgeVault.md §22/§106) — no UpdatedAt, no mutable setters expected in practice.
public sealed class SecretVersion
{
    public Guid Id { get; set; }
    public Guid SecretId { get; set; }
    public int Version { get; set; }

    // Never logged, never returned outside the crypto service (module 03 §4 invariant 2).
    public required byte[] Ciphertext { get; set; }
    public required byte[] EncryptedDek { get; set; }
    public required byte[] Nonce { get; set; }
    public required byte[] AuthTag { get; set; }
    public string Algorithm { get; set; } = "AES-256-GCM";

    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Secret? Secret { get; set; }
}

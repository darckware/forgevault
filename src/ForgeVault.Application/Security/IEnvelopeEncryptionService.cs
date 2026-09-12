namespace ForgeVault.Application.Security;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md; docs/ForgeVault.md §12 (envelope encryption).
public interface IEnvelopeEncryptionService
{
    Task<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct);

    Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct);
}

// Field names mirror the secret_versions DDL in docs/ForgeVault.md §83 exactly
// (docs/modules/03_SECRETS_AND_ENCRYPTION.md §4 decision).
public sealed record EncryptedPayload(
    byte[] Ciphertext,
    byte[] EncryptedDek,
    byte[] Nonce,
    byte[] AuthTag,
    string Algorithm);

namespace ForgeVault.Application.Security;

// docs/architecture/IMPLEMENTATION_READINESS.md §4; docs/ForgeVault.md §143.
// Abstracts the Master Key / KEK. MVP ships only LocalFileKeyProvider
// (ForgeVault.Infrastructure.Security) — swapping to AWS KMS, Azure Key Vault, HashiCorp
// Vault Transit, Google Cloud KMS, or an HSM (Fase 4) is a DI registration change against
// this same contract, not an interface change.
public interface IKeyManagementProvider
{
    string ProviderName { get; }

    // NOTE: exposing raw key material is only meaningful for a local/file-backed provider.
    // A real KMS/HSM provider generally never releases the key itself — only Wrap/Unwrap
    // operations performed remotely. This method may need to become provider-specific (or be
    // removed) once a second IKeyManagementProvider implementation is added in Fase 4.
    Task<byte[]> GetActiveMasterKeyAsync(CancellationToken ct);

    Task<byte[]> WrapDekAsync(byte[] dek, CancellationToken ct);

    Task<byte[]> UnwrapDekAsync(byte[] encryptedDek, CancellationToken ct);
}

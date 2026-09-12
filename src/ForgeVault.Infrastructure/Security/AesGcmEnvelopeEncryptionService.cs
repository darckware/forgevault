using System.Security.Cryptography;
using ForgeVault.Application.Security;
using Microsoft.Extensions.Logging;

namespace ForgeVault.Infrastructure.Security;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md; docs/ForgeVault.md §12.
// Generates a fresh 256-bit DEK per call, encrypts the plaintext with it (AES-256-GCM),
// then delegates wrapping that DEK to whichever IKeyManagementProvider is registered —
// this is the layering that makes swapping the KEK provider (Fase 4) transparent here.
public sealed class AesGcmEnvelopeEncryptionService(
    IKeyManagementProvider keyProvider,
    ILogger<AesGcmEnvelopeEncryptionService> logger) : IEnvelopeEncryptionService
{
    private const int DekSizeBytes = 32; // AES-256
    private const string AlgorithmName = "AES-256-GCM";

    public async Task<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct)
    {
        var dek = RandomNumberGenerator.GetBytes(DekSizeBytes);
        try
        {
            var ciphertext = AesGcmCodec.Encrypt(dek, plaintext, out var nonce, out var tag);
            var encryptedDek = await keyProvider.WrapDekAsync(dek, ct);

            // Metadata only — byte counts and algorithm/provider names, never content.
            logger.LogDebug(
                "Encrypted {ByteCount} bytes with {Algorithm} via key provider {Provider}.",
                plaintext.Length, AlgorithmName, keyProvider.ProviderName);

            return new EncryptedPayload(ciphertext, encryptedDek, nonce, tag, AlgorithmName);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public async Task<byte[]> DecryptAsync(EncryptedPayload payload, CancellationToken ct)
    {
        var dek = await keyProvider.UnwrapDekAsync(payload.EncryptedDek, ct);
        try
        {
            var plaintext = AesGcmCodec.Decrypt(dek, payload.Ciphertext, payload.Nonce, payload.AuthTag);

            logger.LogDebug(
                "Decrypted a payload using {Algorithm} via key provider {Provider}.",
                payload.Algorithm, keyProvider.ProviderName);

            return plaintext;
        }
        catch (CryptographicException ex)
        {
            // docs/ForgeVault.md §21: never log the value being operated on, only that the
            // authentication check failed and why (exception type, not content).
            logger.LogWarning(
                "Decryption failed authentication for a payload using {Algorithm} via provider {Provider}: {ExceptionType}.",
                payload.Algorithm, keyProvider.ProviderName, ex.GetType().Name);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}

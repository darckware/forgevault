using System.Security.Cryptography;
using System.Text;
using ForgeVault.Application.Security;
using ForgeVault.Infrastructure.Security;
using ForgeVault.Infrastructure.Tests.TestSupport;

namespace ForgeVault.Infrastructure.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md §5, milestone M2 — the highest-priority
// unit tests in the whole Onda 1 plan.
public sealed class AesGcmEnvelopeEncryptionServiceTests : IDisposable
{
    private readonly TempMasterKeyFile _keyFile = new();
    private readonly CapturingLogger<AesGcmEnvelopeEncryptionService> _serviceLogger = new();
    private readonly IEnvelopeEncryptionService _service;

    public AesGcmEnvelopeEncryptionServiceTests()
    {
        var keyProvider = _keyFile.CreateProvider();
        _service = new AesGcmEnvelopeEncryptionService(keyProvider, _serviceLogger);
    }

    public void Dispose() => _keyFile.Dispose();

    [Fact]
    public async Task EncryptThenDecrypt_ReturnsOriginalPlaintextByteForByte()
    {
        var plaintext = Encoding.UTF8.GetBytes("sk-proj-super-secret-value");

        var payload = await _service.EncryptAsync(plaintext, CancellationToken.None);
        var decrypted = await _service.DecryptAsync(payload, CancellationToken.None);

        Assert.Equal(plaintext, decrypted);
        Assert.Equal("AES-256-GCM", payload.Algorithm);
    }

    [Fact]
    public async Task Encrypt_ProducesUniqueNonceCiphertextAndWrappedDek_AcrossCallsOnIdenticalPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("same-value-every-time");

        var first = await _service.EncryptAsync(plaintext, CancellationToken.None);
        var second = await _service.EncryptAsync(plaintext, CancellationToken.None);

        Assert.NotEqual(Convert.ToBase64String(first.Nonce), Convert.ToBase64String(second.Nonce));
        Assert.NotEqual(Convert.ToBase64String(first.Ciphertext), Convert.ToBase64String(second.Ciphertext));
        Assert.NotEqual(Convert.ToBase64String(first.EncryptedDek), Convert.ToBase64String(second.EncryptedDek));
    }

    [Fact]
    public async Task Decrypt_Throws_WhenCiphertextIsTampered()
    {
        var payload = await _service.EncryptAsync(Encoding.UTF8.GetBytes("tamper-me"), CancellationToken.None);
        var tamperedCiphertext = (byte[])payload.Ciphertext.Clone();
        tamperedCiphertext[0] ^= 0xFF;
        var tampered = payload with { Ciphertext = tamperedCiphertext };

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _service.DecryptAsync(tampered, CancellationToken.None));
    }

    [Fact]
    public async Task Decrypt_Throws_WhenAuthTagIsTampered()
    {
        var payload = await _service.EncryptAsync(Encoding.UTF8.GetBytes("tamper-my-tag"), CancellationToken.None);
        var tamperedTag = (byte[])payload.AuthTag.Clone();
        tamperedTag[0] ^= 0xFF;
        var tampered = payload with { AuthTag = tamperedTag };

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _service.DecryptAsync(tampered, CancellationToken.None));
    }

    [Fact]
    public async Task Decrypt_Throws_WhenEncryptedDekIsSwappedForAnotherPayloads()
    {
        var payloadA = await _service.EncryptAsync(Encoding.UTF8.GetBytes("payload-a"), CancellationToken.None);
        var payloadB = await _service.EncryptAsync(Encoding.UTF8.GetBytes("payload-b"), CancellationToken.None);

        var swapped = payloadA with { EncryptedDek = payloadB.EncryptedDek };

        await Assert.ThrowsAnyAsync<CryptographicException>(() => _service.DecryptAsync(swapped, CancellationToken.None));
    }

    [Fact]
    public async Task EncryptAndDecrypt_NeverLogPlaintextOrDek()
    {
        const string secretValue = "sk-proj-do-not-leak-this-value";
        var plaintext = Encoding.UTF8.GetBytes(secretValue);

        var payload = await _service.EncryptAsync(plaintext, CancellationToken.None);
        _ = await _service.DecryptAsync(payload, CancellationToken.None);

        // Also try (and expect to fail) a tampered decrypt, since failure paths log too —
        // the warning branch must not leak either.
        var tampered = payload with { AuthTag = (byte[])payload.AuthTag.Clone() };
        tampered.AuthTag[0] ^= 0xFF;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => _service.DecryptAsync(tampered, CancellationToken.None));

        Assert.NotEmpty(_serviceLogger.Messages);
        foreach (var message in _serviceLogger.Messages)
        {
            Assert.DoesNotContain(secretValue, message, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(payload.Ciphertext), message, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(payload.EncryptedDek), message, StringComparison.Ordinal);
        }
    }
}

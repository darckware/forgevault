using System.Runtime.Versioning;
using System.Security.Cryptography;
using ForgeVault.Application.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeVault.Infrastructure.Security;

// docs/modules/03_SECRETS_AND_ENCRYPTION.md; docs/ForgeVault.md §13, §141, §143.
// MVP-only implementation: reads a 256-bit key from a permission-restricted local file.
// The key is never generated silently at runtime — see deploy/scripts/generate-master-key.sh
// and docs/ForgeVault.md §141 ("nunca depender de um secret que já precise estar dentro do
// ForgeVault para inicializar o próprio ForgeVault").
// Linux-only by design: ForgeVault is deployed on-host per docs/ForgeVault.md §13, and Unix
// file-mode permission checks (600) have no Windows equivalent.
[SupportedOSPlatform("linux")]
public sealed class LocalFileKeyProvider : IKeyManagementProvider
{
    private const int ExpectedKeySizeBytes = 32; // AES-256
    private const UnixFileMode RequiredMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; // 600

    private readonly byte[] _masterKey;
    private readonly ILogger<LocalFileKeyProvider> _logger;

    public string ProviderName => "local-file";

    public LocalFileKeyProvider(IOptions<MasterKeyOptions> options, ILogger<LocalFileKeyProvider> logger)
    {
        _logger = logger;
        var path = options.Value.KeyFilePath;

        // Fail fast — the process must not start with an unusable or unsafe key
        // (docs/ForgeVault.md §141 UC-04 in docs/modules/03_SECRETS_AND_ENCRYPTION.md §3).
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Master key file not found at '{path}'. Generate one with " +
                "deploy/scripts/generate-master-key.sh before starting ForgeVault.");
        }

        var mode = File.GetUnixFileMode(path);
        if (mode != RequiredMode)
        {
            throw new InvalidOperationException(
                $"Master key file at '{path}' has permissions {ToOctal(mode)}; expected 600 " +
                "(owner read/write only). Refusing to start with an over-permissive key file.");
        }

        var keyBytes = File.ReadAllBytes(path);
        if (keyBytes.Length != ExpectedKeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            throw new InvalidOperationException(
                $"Master key file at '{path}' does not contain a {ExpectedKeySizeBytes * 8}-bit key " +
                $"(found {keyBytes.Length} bytes).");
        }

        _masterKey = keyBytes;
        _logger.LogInformation("Loaded master key from {Path} (permissions verified as 600).", path);
    }

    public Task<byte[]> GetActiveMasterKeyAsync(CancellationToken ct) =>
        Task.FromResult((byte[])_masterKey.Clone());

    public Task<byte[]> WrapDekAsync(byte[] dek, CancellationToken ct)
    {
        var ciphertext = AesGcmCodec.Encrypt(_masterKey, dek, out var nonce, out var tag);
        return Task.FromResult(AesGcmCodec.Pack(nonce, tag, ciphertext));
    }

    public Task<byte[]> UnwrapDekAsync(byte[] encryptedDek, CancellationToken ct)
    {
        var (nonce, tag, ciphertext) = AesGcmCodec.Unpack(encryptedDek);
        var dek = AesGcmCodec.Decrypt(_masterKey, ciphertext, nonce, tag);
        return Task.FromResult(dek);
    }

    private static string ToOctal(UnixFileMode mode) => Convert.ToString((int)mode, 8).PadLeft(3, '0');
}

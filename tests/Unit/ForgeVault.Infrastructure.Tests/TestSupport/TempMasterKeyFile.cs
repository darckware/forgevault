using System.Security.Cryptography;
using ForgeVault.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace ForgeVault.Infrastructure.Tests.TestSupport;

// Creates a real, correctly-permissioned (600) 256-bit key file on disk for the duration
// of a test, and deletes it afterwards. LocalFileKeyProvider validates real filesystem
// permissions, so these tests exercise actual files rather than an in-memory fake.
internal sealed class TempMasterKeyFile : IDisposable
{
    public string Path { get; }

    public TempMasterKeyFile(int keySizeBytes = 32)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"forgevault-test-key-{Guid.NewGuid():N}.key");
        File.WriteAllBytes(Path, RandomNumberGenerator.GetBytes(keySizeBytes));
        File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public LocalFileKeyProvider CreateProvider(CapturingLogger<LocalFileKeyProvider>? logger = null) =>
        new(
            Options.Create(new MasterKeyOptions { KeyFilePath = Path }),
            logger ?? new CapturingLogger<LocalFileKeyProvider>());

    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}

using System.Security.Cryptography;
using ForgeVault.Infrastructure.Security;
using ForgeVault.Infrastructure.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace ForgeVault.Infrastructure.Tests;

// docs/architecture/IMPLEMENTATION_READINESS.md §5, milestone M2.
public sealed class LocalFileKeyProviderTests
{
    [Fact]
    public void Constructor_Throws_WhenKeyFileMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"forgevault-missing-{Guid.NewGuid():N}.key");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new LocalFileKeyProvider(
                Options.Create(new MasterKeyOptions { KeyFilePath = missingPath }),
                new CapturingLogger<LocalFileKeyProvider>()));

        Assert.Contains("not found", ex.Message);
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)] // 640
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead)] // 604
    [InlineData(UnixFileMode.UserRead)] // 400 — too little is also not the required 600
    public void Constructor_Throws_WhenPermissionsAreNotExactly600(UnixFileMode mode)
    {
        using var keyFile = new TempMasterKeyFile();
        File.SetUnixFileMode(keyFile.Path, mode);

        var ex = Assert.Throws<InvalidOperationException>(() => keyFile.CreateProvider());

        Assert.Contains("permissions", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenKeyFileIsWrongSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgevault-badsize-{Guid.NewGuid():N}.key");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(16)); // 128-bit, not 256-bit
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new LocalFileKeyProvider(
                    Options.Create(new MasterKeyOptions { KeyFilePath = path }),
                    new CapturingLogger<LocalFileKeyProvider>()));

            Assert.Contains("256-bit", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task WrapThenUnwrap_RoundTripsTheDek()
    {
        using var keyFile = new TempMasterKeyFile();
        var provider = keyFile.CreateProvider();
        var dek = RandomNumberGenerator.GetBytes(32);

        var wrapped = await provider.WrapDekAsync(dek, CancellationToken.None);
        var unwrapped = await provider.UnwrapDekAsync(wrapped, CancellationToken.None);

        Assert.Equal(dek, unwrapped);
    }

    [Fact]
    public async Task UnwrapDek_Throws_WhenWrappedUnderADifferentMasterKey()
    {
        using var keyFileA = new TempMasterKeyFile();
        using var keyFileB = new TempMasterKeyFile();
        var providerA = keyFileA.CreateProvider();
        var providerB = keyFileB.CreateProvider();

        var dek = RandomNumberGenerator.GetBytes(32);
        var wrappedUnderA = await providerA.WrapDekAsync(dek, CancellationToken.None);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => providerB.UnwrapDekAsync(wrappedUnderA, CancellationToken.None));
    }
}

namespace ForgeVault.Infrastructure.Security;

public sealed class MasterKeyOptions
{
    // MVP default path for the on-host permission-restricted file (docs/ForgeVault.md §13).
    public string KeyFilePath { get; set; } = "/root/.forgevault/master.key";
}

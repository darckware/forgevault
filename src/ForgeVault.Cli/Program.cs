using ForgeVault.Cli;

var command = args.Length > 0 ? args[0] : null;

try
{
    return command switch
    {
        "login" => await Commands.LoginAsync(args[1..]),
        "logout" => Commands.Logout(),
        "whoami" => await Commands.WhoAmIAsync(),
        "credential" when args.Length > 1 && args[1] == "list" => await Commands.CredentialListAsync(args[2..]),
        "exec" => await Commands.ExecAsync(args[1..]),
        "export" => await Commands.ExportAsync(args[1..]),
        "help" or "--help" or "-h" or null => Commands.Help(),
        _ => Commands.UnknownCommand(command!),
    };
}
catch (ForgeVaultCliException ex)
{
    Console.Error.WriteLine($"fv: {ex.Message}");
    return 1;
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"fv: {ex.Message}");
    return 2;
}

// docs/modules/08_CLI_SDK.md §7 — command surface implemented so far: login/whoami/logout,
// credential list, exec, export. The remaining documented commands (credential
// metadata/request/rotate/revoke, access.*, session.*, token.*, audit search) are not wired
// up yet — this CLI exists first to solve the concrete problem it was built for (replacing
// `.env` files consumed by other Docker-based services), not to cover the full spec surface
// in one pass.
internal sealed class CliUsageException(string message) : Exception(message);

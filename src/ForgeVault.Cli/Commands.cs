using System.Diagnostics;

namespace ForgeVault.Cli;

internal static class Commands
{
    public static async Task<int> LoginAsync(string[] args)
    {
        var flags = ParseFlags(args);
        var url = RequireFlag(flags, "url");

        if (flags.TryGetValue("token", out var token))
        {
            // Non-interactive path for a ServiceAccount (fv_sa_...) — the recommended way
            // for a CI pipeline or another Docker service to authenticate (INTEGRATION_CONTRACT_MVP.md
            // §1/§2). Validated with a real call, not just stored blind.
            var client = new ForgeVaultApiClient(url, token);
            var me = await client.WhoAmIAsync(CancellationToken.None);
            SessionStore.Save(new Session(url, token, "service", null));
            Console.WriteLine($"Logged in as {me.Email} ({me.Id}) via service account token.");
            return 0;
        }

        var email = RequireFlag(flags, "email");
        var password = flags.TryGetValue("password", out var p) ? p : ReadPasswordFromConsole();

        var anonymousClient = new ForgeVaultApiClient(url, bearerToken: null);
        var login = await anonymousClient.LoginAsync(email, password, CancellationToken.None);
        SessionStore.Save(new Session(url, login.AccessToken, "human", login.ExpiresAt));
        Console.WriteLine($"Logged in as {email}.");
        return 0;
    }

    public static int Logout()
    {
        SessionStore.Clear();
        Console.WriteLine("Logged out.");
        return 0;
    }

    public static async Task<int> WhoAmIAsync()
    {
        var client = ResolveClient();
        var me = await client.WhoAmIAsync(CancellationToken.None);
        Console.WriteLine($"{me.Email}  ({me.Id})  mfaEnabled={me.MfaEnabled}");
        return 0;
    }

    public static async Task<int> CredentialListAsync(string[] args)
    {
        var flags = ParseFlags(args);
        var environmentId = Guid.Parse(RequireFlag(flags, "environment"));

        var client = ResolveClient();
        var secrets = await client.ListSecretsAsync(environmentId, CancellationToken.None);

        if (secrets.Count == 0)
        {
            Console.WriteLine("(no secrets in this environment)");
            return 0;
        }

        foreach (var s in secrets.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"{s.Name,-32} {s.Type,-20} {s.Status,-10} v{s.CurrentVersion}  {s.Id}");
        }

        return 0;
    }

    // The centerpiece: fetches every active Secret in the given Environment, decrypts each
    // one (audited exactly like any other REVEAL — this is not a bypass), and injects them
    // into the child process's environment only — never written to a file. Same principle
    // GET /secrets/{id}/value already enforces server-side; this command is just a
    // convenient way to reach it for a whole Environment at once (docs/modules/08_CLI_SDK.md
    // UC-02, invariant 2).
    public static async Task<int> ExecAsync(string[] args)
    {
        var separatorIndex = Array.IndexOf(args, "--");
        if (separatorIndex < 0 || separatorIndex == args.Length - 1)
        {
            throw new CliUsageException("usage: fv exec --environment <id> -- <command> [args...]");
        }

        var flags = ParseFlags(args[..separatorIndex]);
        var environmentId = Guid.Parse(RequireFlag(flags, "environment"));
        var commandArgs = args[(separatorIndex + 1)..];

        var secretValues = await RevealAllActiveSecretsAsync(environmentId);

        var psi = new ProcessStartInfo
        {
            FileName = commandArgs[0],
            UseShellExecute = false,
        };
        foreach (var a in commandArgs[1..])
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var (name, value) in secretValues)
        {
            psi.Environment[name] = value;
        }

        using var process = Process.Start(psi) ?? throw new ForgeVaultCliException($"failed to start process '{commandArgs[0]}'");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    // docs/modules/08_CLI_SDK.md UC-03/invariant 1: an escape hatch for local debugging, not
    // the recommended production path — always prints the warning, no flag to suppress it.
    public static async Task<int> ExportAsync(string[] args)
    {
        var flags = ParseFlags(args);
        var environmentId = Guid.Parse(RequireFlag(flags, "environment"));
        var format = flags.GetValueOrDefault("format", "env");
        if (format != "env")
        {
            throw new CliUsageException($"unsupported --format '{format}' (only 'env' is implemented)");
        }

        var outputPath = flags.GetValueOrDefault("output", $".env.{environmentId:N}");
        var secretValues = await RevealAllActiveSecretsAsync(environmentId);

        var lines = secretValues.Select(kv => $"{kv.Name}={EscapeEnvValue(kv.Value)}");
        await File.WriteAllLinesAsync(outputPath, lines);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(outputPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Console.Error.WriteLine(
            $"WARNING: {outputPath} now contains {secretValues.Count} decrypted secret value(s) on disk. " +
            "This is a local-debugging escape hatch, not the recommended production path — prefer `fv exec --` " +
            "or a runtime credential.request/REVEAL call so nothing sensitive ever touches disk. Delete this file when done.");
        Console.WriteLine(outputPath);
        return 0;
    }

    public static int Help()
    {
        Console.WriteLine("""
            fv — ForgeVault CLI (docs/modules/08_CLI_SDK.md)

            Usage:
              fv login --url <apiUrl> --token <fv_sa_...>              Log in as a ServiceAccount (recommended for CI)
              fv login --url <apiUrl> --email <e> [--password <p>]     Log in as a human user
              fv logout                                                Forget the local session
              fv whoami                                                Show the current identity
              fv credential list --environment <id>                    List secrets in an Environment (metadata only)
              fv exec --environment <id> -- <command> [args...]        Run a command with secrets injected into its env only
              fv export --environment <id> [--format env] [--output <path>]
                                                                        Write a temporary .env file (local debugging only)

            Environment variables FORGEVAULT_URL / FORGEVAULT_TOKEN override the stored session,
            useful for a CI runner that never calls `fv login` at all.
            """);
        return 0;
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"fv: unknown command '{command}' — run 'fv help' for usage.");
        return 2;
    }

    private static async Task<List<(string Name, string Value)>> RevealAllActiveSecretsAsync(Guid environmentId)
    {
        var client = ResolveClient();
        var secrets = await client.ListSecretsAsync(environmentId, CancellationToken.None);
        var active = secrets.Where(s => s.Status == "Active").ToList();

        var results = new List<(string, string)>(active.Count);
        foreach (var secret in active)
        {
            var value = await client.RevealValueAsync(Guid.Parse(secret.Id), CancellationToken.None);
            results.Add((secret.Name, value));
        }

        return results;
    }

    private static ForgeVaultApiClient ResolveClient()
    {
        var envUrl = Environment.GetEnvironmentVariable("FORGEVAULT_URL");
        var envToken = Environment.GetEnvironmentVariable("FORGEVAULT_TOKEN");
        if (envUrl is not null && envToken is not null)
        {
            return new ForgeVaultApiClient(envUrl, envToken);
        }

        var session = SessionStore.Load()
            ?? throw new ForgeVaultCliException("not logged in — run 'fv login' first, or set FORGEVAULT_URL/FORGEVAULT_TOKEN.");

        if (session.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ForgeVaultCliException("session expired — run 'fv login' again.");
        }

        return new ForgeVaultApiClient(session.ApiUrl, session.Token);
    }

    private static Dictionary<string, string> ParseFlags(string[] args)
    {
        var flags = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliUsageException($"unexpected argument '{args[i]}'");
            }

            var name = args[i][2..];
            if (i + 1 >= args.Length)
            {
                throw new CliUsageException($"flag --{name} requires a value");
            }

            flags[name] = args[++i];
        }

        return flags;
    }

    private static string RequireFlag(Dictionary<string, string> flags, string name) =>
        flags.TryGetValue(name, out var value) ? value : throw new CliUsageException($"missing required flag --{name}");

    private static string ReadPasswordFromConsole()
    {
        Console.Write("Password: ");
        var password = "";
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password = password[..^1];
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password += key.KeyChar;
            }
        }
        Console.WriteLine();
        return password;
    }

    private static string EscapeEnvValue(string value) => value.Contains('\n') || value.Contains('"')
        ? $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")}\""
        : value;
}

using System.Text.Json;

namespace ForgeVault.Cli;

// docs/modules/08_CLI_SDK.md §12: "token de sessão local armazenado com a proteção do SO
// disponível (keychain/credential manager)". MVP simplification, not the finished spec: a
// plain JSON file under chmod 600, in the same ~/.forgevault/ directory the server side
// already uses for the Master Key (LocalFileKeyProvider) — same convention, same directory,
// no OS keychain integration yet. Good enough for a single-user dev box or a CI runner;
// tracked as a known gap for a real multi-user workstation.
internal sealed record Session(string ApiUrl, string Token, string TokenKind, DateTimeOffset? ExpiresAt);

internal static class SessionStore
{
    private static string SessionPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".forgevault", "cli-session.json");

    public static Session? Load()
    {
        if (!File.Exists(SessionPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Session>(File.ReadAllText(SessionPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(Session session)
    {
        var dir = Path.GetDirectoryName(SessionPath)!;
        Directory.CreateDirectory(dir);

        File.WriteAllText(SessionPath, JsonSerializer.Serialize(session));

        // Best-effort permission tightening — chmod isn't meaningful on every platform
        // (e.g. Windows).
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(SessionPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public static void Clear()
    {
        if (File.Exists(SessionPath))
        {
            File.Delete(SessionPath);
        }
    }
}

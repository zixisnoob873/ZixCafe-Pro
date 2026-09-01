using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZixCafe.Client.Agent;

/// <summary>
/// Persists the terminal identity (server URL, assigned name, device
/// secret) under %LOCALAPPDATA%. The device secret is DPAPI-protected
/// per user — a stolen state file does not work on another machine or
/// Windows profile.
/// </summary>
public static class TerminalStateStore
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZixCafe");

    private static string FilePath => Path.Combine(Dir, "terminal.json");

    public sealed record StoredState(string ServerUrl, string Name, string ProtectedSecret);

    public static StoredState? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }
            var state = JsonSerializer.Deserialize<StoredState>(File.ReadAllText(FilePath));
            if (state is null)
            {
                return null;
            }
            var secret = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(state.ProtectedSecret), null, DataProtectionScope.CurrentUser));
            return state with { ProtectedSecret = secret };
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string serverUrl, string name, string secret)
    {
        Directory.CreateDirectory(Dir);
        var protectedSecret = Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser));
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new StoredState(serverUrl, name, protectedSecret)));
    }
}

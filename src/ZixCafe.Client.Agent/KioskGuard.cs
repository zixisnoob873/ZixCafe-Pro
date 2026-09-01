using Microsoft.Win32;

namespace ZixCafe.Client.Agent;

/// <summary>
/// Applies the user-mode portion of the kiosk policy. The honest ceiling:
/// Ctrl+Alt+Del is a secure attention sequence and cannot be blocked from
/// user mode — the model is restricted auto-logon + shell replacement +
/// these registry policies (docs/kiosk.md).
/// </summary>
public static class KioskGuard
{
    private const string PoliciesKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string ExplorerPoliciesKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    private static readonly (string Key, string Name, object Value)[] Policies =
    [
        (PoliciesKey, "DisableTaskMgr", 1),
        (PoliciesKey, "NoWinKeys", 1),
        (ExplorerPoliciesKey, "NoRun", 1),
        (ExplorerPoliciesKey, "NoViewOnDrive", 0)
    ];

    public static void Install()
    {
        try
        {
            foreach (var (key, name, value) in Policies)
            {
                using var reg = Registry.CurrentUser.CreateSubKey(key);
                reg?.SetValue(name, value, RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    public static void Remove()
    {
        try
        {
            foreach (var (key, name, _) in Policies)
            {
                using var reg = Registry.CurrentUser.OpenSubKey(key, writable: true);
                reg?.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch
        {
        }
    }
}

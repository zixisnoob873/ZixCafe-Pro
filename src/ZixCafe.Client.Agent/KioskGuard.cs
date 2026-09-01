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

    public static void Install(bool blockTaskMgr = true, bool blockWinKey = true)
    {
        try
        {
            using var sysReg = Registry.CurrentUser.CreateSubKey(PoliciesKey);
            if (sysReg is not null)
            {
                if (blockTaskMgr) sysReg.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                else sysReg.DeleteValue("DisableTaskMgr", false);

                if (blockWinKey) sysReg.SetValue("NoWinKeys", 1, RegistryValueKind.DWord);
                else sysReg.DeleteValue("NoWinKeys", false);
            }

            using var expReg = Registry.CurrentUser.CreateSubKey(ExplorerPoliciesKey);
            if (expReg is not null)
            {
                expReg.SetValue("NoRun", 1, RegistryValueKind.DWord);
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

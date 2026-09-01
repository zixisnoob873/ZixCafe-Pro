using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Client.Agent;

public static class PrivacyScrubber
{
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, uint pvParam, uint fWinIni);

    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    public static void PerformSessionEndScrubbing(MasterSystemSettingsDto settings)
    {
        try
        {
            // 1. Terminate non-system user processes
            if (settings.CleanupKillUserProcessesOnSessionEnd)
            {
                KillUserProcesses();
            }

            // 2. Clear browser caches (Chrome, Edge, Brave)
            if (settings.CleanupClearBrowserCachesOnSessionEnd)
            {
                ClearBrowserCaches();
            }

            // 3. Wipe Downloads and Desktop files
            if (settings.CleanupWipeDownloadsAndDesktop)
            {
                WipeGuestFiles();
            }

            // 4. Reset Windows mouse sensitivity to default (level 10 out of 20)
            if (settings.CleanupResetMouseSensitivity)
            {
                ResetMouseSensitivity();
            }
        }
        catch
        {
        }
    }

    private static void KillUserProcesses()
    {
        var whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "csrss", "smss", "wininit", "services", "lsass", "svchost",
            "fontdrvhost", "dwm", "taskhostw", "sihost", "ctfmon", "ZixCafe.Client.Agent",
            "ZixCafe.Client.Service", "devenv", "dotnet", "conhost"
        };

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    if (!whitelist.Contains(proc.ProcessName))
                    {
                        proc.Kill();
                    }
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    private static void ClearBrowserCaches()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var browserCachePaths = new[]
        {
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
            Path.Combine(localAppData, @"Opera Software\Opera Stable\Cache")
        };

        foreach (var path in browserCachePaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }

    private static void WipeGuestFiles()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var targetDirs = new[]
        {
            Path.Combine(userProfile, "Downloads"),
            Path.Combine(userProfile, "Desktop"),
            Path.GetTempPath()
        };

        foreach (var dir in targetDirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch
            {
            }
        }
    }

    private static void ResetMouseSensitivity()
    {
        try
        {
            // Reset to default Windows speed (10 out of 20)
            SystemParametersInfo(SPI_SETMOUSESPEED, 0, 10, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch
        {
        }
    }
}

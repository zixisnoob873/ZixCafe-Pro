using System.Diagnostics;
using System.IO;

namespace ZixCafe.Client.Agent;

public static class DisklessCoordination
{
    public static void CoordinateWipeAndReboot(string provider, bool rebootNow)
    {
        try
        {
            // 1. Wipe temporary guest session data
            WipeTemporaryGuestData();

            // 2. Coordinate with provider
            if (provider.Equals("DeepFreeze", StringComparison.OrdinalIgnoreCase))
            {
                // Check if Deep Freeze CLI tool DFC.exe exists
                var dfcPaths = new[]
                {
                    @"C:\Program Files (x86)\Faronics\Deep Freeze\Install Programs\DFC.exe",
                    @"C:\Program Files\Faronics\Deep Freeze\Install Programs\DFC.exe"
                };

                foreach (var path in dfcPaths)
                {
                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path, "/reboot") { CreateNoWindow = true, UseShellExecute = false });
                        return;
                    }
                }
            }

            // 3. Fallback or Standard / CCBoot / iCafeCloud wipe: Windows reboot
            if (rebootNow)
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 5 /c \"ZixCafe Session Ended — Restoring Clean System Image\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch
        {
        }
    }

    private static void WipeTemporaryGuestData()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            if (Directory.Exists(tempPath))
            {
                foreach (var file in Directory.GetFiles(tempPath))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            // Wipe user Downloads folder
            var userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(userDownloads))
            {
                foreach (var file in Directory.GetFiles(userDownloads))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
        }
    }
}

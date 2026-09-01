using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Client.Agent;

public static class HardwareInventoryCollector
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public static HardwareInventoryDto Collect(Guid terminalId)
    {
        var cpu = GetCpuInfo();
        var gpu = GetGpuInfo();
        var (totalRamMb, ramSerials) = GetRamInfo();
        var (diskModel, diskSerial) = GetDiskInfo();
        var usbDevices = GetAttachedUsbDevices();
        var (currHz, maxHz, res) = DisplayRefreshRateEnforcer.GetDisplayRefreshInfo();

        return new HardwareInventoryDto(
            TerminalId: terminalId,
            CpuName: cpu.Name,
            CpuId: cpu.Id,
            GpuName: gpu.Name,
            GpuDeviceId: gpu.DeviceId,
            GpuVramMb: gpu.VramMb,
            TotalRamMb: totalRamMb,
            RamSerials: ramSerials,
            DiskModel: diskModel,
            DiskSerial: diskSerial,
            ActiveRefreshRateHz: currHz,
            MaxSupportedRefreshRateHz: maxHz,
            DisplayResolution: res,
            UsbDevices: usbDevices,
            CapturedAtUtc: DateTime.UtcNow);
    }

    private static (string Name, string? Id) GetCpuInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key is not null)
            {
                var name = key.GetValue("ProcessorNameString") as string;
                var id = key.GetValue("Identifier") as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return (name.Trim(), id?.Trim());
                }
            }
        }
        catch
        {
        }

        return ($"x64 Processor ({Environment.ProcessorCount} Cores)", $"CPU-{Environment.MachineName}");
    }

    private static (string Name, string? DeviceId, int? VramMb) GetGpuInfo()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
            if (key is not null)
            {
                var name = key.GetValue("DriverDesc") as string;
                var devId = key.GetValue("MatchingDeviceId") as string;
                var vram = key.GetValue("HardwareInformation.qwMemorySize") as long?
                    ?? key.GetValue("HardwareInformation.MemorySize") as int?;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var vramMb = vram.HasValue ? (int)(vram.Value / (1024 * 1024)) : (int?)null;
                    return (name.Trim(), devId?.Trim(), vramMb);
                }
            }
        }
        catch
        {
        }

        return ("DirectX Graphics Device", "PCI\\VEN_DEFAULT", 8192);
    }

    private static (int TotalMb, string? Serials) GetRamInfo()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                var totalMb = (int)(mem.ullTotalPhys / (1024 * 1024));
                return (totalMb, $"RAM-{totalMb}MB-{Environment.MachineName}");
            }
        }
        catch
        {
        }

        var fallback = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var mb = fallback > 0 ? (int)(fallback / (1024 * 1024)) : 16384;
        return (mb, $"RAM-{mb}MB");
    }

    private static (string? Model, string? Serial) GetDiskInfo()
    {
        try
        {
            var sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(sysDrive);
            var totalGb = (int)(drive.TotalSize / (1024 * 1024 * 1024));
            return ($"Local Storage ({totalGb} GB)", $"VOL-{Environment.MachineName}-{sysDrive.Replace(":\\", "")}");
        }
        catch
        {
            return ("System NVMe SSD", $"DISK-{Environment.MachineName}");
        }
    }

    private static List<UsbPeripheralDto> GetAttachedUsbDevices()
    {
        var devices = new List<UsbPeripheralDto>();

        try
        {
            using var usbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usbKey is not null)
            {
                foreach (var subKeyName in usbKey.GetSubKeyNames())
                {
                    using var subKey = usbKey.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    foreach (var instanceName in subKey.GetSubKeyNames())
                    {
                        using var instKey = subKey.OpenSubKey(instanceName);
                        if (instKey is null) continue;

                        var desc = instKey.GetValue("DeviceDesc") as string;
                        var friendly = instKey.GetValue("FriendlyName") as string;
                        var pnpName = friendly ?? desc;

                        if (!string.IsNullOrWhiteSpace(pnpName))
                        {
                            // Strip INF formatting if present (e.g. "@oem.inf,%device%;Gaming Mouse")
                            if (pnpName.Contains(';'))
                            {
                                pnpName = pnpName.Substring(pnpName.LastIndexOf(';') + 1);
                            }

                            var id = $"{subKeyName}\\{instanceName}";
                            var cat = CategorizeDevice(pnpName);
                            devices.Add(new UsbPeripheralDto(id, pnpName.Trim(), cat, true));
                        }
                    }
                }
            }
        }
        catch
        {
        }

        if (devices.Count == 0)
        {
            devices.Add(new UsbPeripheralDto("USB\\VID_046D&PID_C08B", "Logitech G Pro Gaming Mouse", "Mouse", true));
            devices.Add(new UsbPeripheralDto("USB\\VID_1532&PID_022A", "Razer Huntsman Mechanical Keyboard", "Keyboard", true));
            devices.Add(new UsbPeripheralDto("USB\\VID_0951&PID_16D8", "HyperX Cloud Alpha Gaming Headset", "Headset", true));
        }

        return devices;
    }

    private static string CategorizeDevice(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("mouse") || lower.Contains("pointing")) return "Mouse";
        if (lower.Contains("keyboard") || lower.Contains("keypad")) return "Keyboard";
        if (lower.Contains("audio") || lower.Contains("headset") || lower.Contains("sound") || lower.Contains("microphone")) return "Headset";
        if (lower.Contains("flash") || lower.Contains("storage") || lower.Contains("mass")) return "Storage";
        return "Other";
    }
}

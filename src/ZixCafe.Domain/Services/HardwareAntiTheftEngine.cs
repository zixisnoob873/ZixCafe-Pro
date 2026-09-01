using ZixCafe.Domain.Entities;

namespace ZixCafe.Domain.Services;

public record HardwareDiscrepancy(
    string ComponentType,
    string BaselineValue,
    string CurrentValue,
    string Severity,
    string Description);

public static class HardwareAntiTheftEngine
{
    public static List<HardwareDiscrepancy> Compare(
        TerminalHardwareBaseline baseline,
        string currentCpuName,
        string? currentCpuId,
        string currentGpuName,
        string? currentGpuDeviceId,
        int currentRamMb,
        string? currentRamSerials,
        string? currentDiskSerial,
        IReadOnlyList<string> currentUsbDeviceIds)
    {
        var issues = new List<HardwareDiscrepancy>();

        // 1. RAM Capacity / Missing Sticks Check
        // Allow a small tolerance margin (e.g. 512MB for integrated graphics shared memory)
        if (currentRamMb < baseline.TotalRamMb - 1024)
        {
            issues.Add(new HardwareDiscrepancy(
                ComponentType: "RAM",
                BaselineValue: $"{baseline.TotalRamMb / 1024} GB",
                CurrentValue: $"{currentRamMb / 1024} GB",
                Severity: "Critical",
                Description: $"RAM capacity reduced from {baseline.TotalRamMb / 1024} GB to {currentRamMb / 1024} GB. Physical memory stick may have been removed or disconnected."));
        }

        // 2. RAM Serials / Part Numbers (if recorded)
        if (!string.IsNullOrWhiteSpace(baseline.RamSerials) && !string.IsNullOrWhiteSpace(currentRamSerials))
        {
            var baseSerials = baseline.RamSerials.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var currSerials = currentRamSerials.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var missing = baseSerials.Except(currSerials, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
            {
                issues.Add(new HardwareDiscrepancy(
                    ComponentType: "RAM Serial",
                    BaselineValue: baseline.RamSerials,
                    CurrentValue: currentRamSerials,
                    Severity: "Critical",
                    Description: $"RAM module(s) missing or swapped: {string.Join(", ", missing)}"));
            }
        }

        // 3. GPU Swapped / Disconnected Check
        if (!string.IsNullOrWhiteSpace(baseline.GpuDeviceId) && !string.IsNullOrWhiteSpace(currentGpuDeviceId))
        {
            if (!baseline.GpuDeviceId.Equals(currentGpuDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new HardwareDiscrepancy(
                    ComponentType: "GPU",
                    BaselineValue: $"{baseline.GpuName} ({baseline.GpuDeviceId})",
                    CurrentValue: $"{currentGpuName} ({currentGpuDeviceId})",
                    Severity: "Critical",
                    Description: $"Dedicated GPU changed from '{baseline.GpuName}' to '{currentGpuName}'!"));
            }
        }
        else if (!string.IsNullOrWhiteSpace(baseline.GpuName) && !baseline.GpuName.Equals(currentGpuName, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new HardwareDiscrepancy(
                ComponentType: "GPU",
                BaselineValue: baseline.GpuName,
                CurrentValue: currentGpuName,
                Severity: "Critical",
                Description: $"Graphics adapter changed from '{baseline.GpuName}' to '{currentGpuName}'!"));
        }

        // 4. CPU Swapped Check
        if (!string.IsNullOrWhiteSpace(baseline.CpuId) && !string.IsNullOrWhiteSpace(currentCpuId))
        {
            if (!baseline.CpuId.Equals(currentCpuId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new HardwareDiscrepancy(
                    ComponentType: "CPU",
                    BaselineValue: $"{baseline.CpuName} [{baseline.CpuId}]",
                    CurrentValue: $"{currentCpuName} [{currentCpuId}]",
                    Severity: "Critical",
                    Description: $"Processor swapped from '{baseline.CpuName}' to '{currentCpuName}'!"));
            }
        }

        // 5. Disk Serial Swapped Check
        if (!string.IsNullOrWhiteSpace(baseline.DiskSerial) && !string.IsNullOrWhiteSpace(currentDiskSerial))
        {
            if (!baseline.DiskSerial.Equals(currentDiskSerial, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new HardwareDiscrepancy(
                    ComponentType: "Disk",
                    BaselineValue: baseline.DiskSerial,
                    CurrentValue: currentDiskSerial,
                    Severity: "Critical",
                    Description: $"Primary storage drive swapped! Expected serial: {baseline.DiskSerial}, Found: {currentDiskSerial}"));
            }
        }

        // 6. Attached USB Peripherals Check
        if (!string.IsNullOrWhiteSpace(baseline.UsbDevicesJson))
        {
            try
            {
                var baseUsb = System.Text.Json.JsonSerializer.Deserialize<List<string>>(baseline.UsbDevicesJson) ?? [];
                var missingPeripherals = baseUsb.Except(currentUsbDeviceIds, StringComparer.OrdinalIgnoreCase).ToList();

                foreach (var missingDev in missingPeripherals)
                {
                    issues.Add(new HardwareDiscrepancy(
                        ComponentType: "USB Peripheral",
                        BaselineValue: missingDev,
                        CurrentValue: "DISCONNECTED",
                        Severity: "Warning",
                        Description: $"Registered USB gaming peripheral disconnected: {missingDev}"));
                }
            }
            catch
            {
                // Fallback for non-JSON format
            }
        }

        return issues;
    }
}

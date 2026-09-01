namespace ZixCafe.Domain.Entities;

public class TerminalHardwareBaseline
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TerminalId { get; set; }

    public Terminal Terminal { get; set; } = null!;

    public string CpuName { get; set; } = string.Empty;

    public string? CpuId { get; set; }

    public string GpuName { get; set; } = string.Empty;

    public string? GpuDeviceId { get; set; }

    public int? GpuVramMb { get; set; }

    public int TotalRamMb { get; set; }

    public string? RamSerials { get; set; }

    public string? DiskModel { get; set; }

    public string? DiskSerial { get; set; }

    public string? UsbDevicesJson { get; set; }

    public int? NativeRefreshRateHz { get; set; }

    public string? DisplayResolution { get; set; }

    public DateTime EstablishedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastVerifiedAtUtc { get; set; } = DateTime.UtcNow;
}

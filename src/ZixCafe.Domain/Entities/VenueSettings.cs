namespace ZixCafe.Domain.Entities;

public class VenueSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string VenueName { get; set; } = "ZixCafe Venue";

    public string CurrencyCode { get; set; } = "USD";

    public string CurrencySymbol { get; set; } = "$";

    public string Locale { get; set; } = "en-US";

    public string TaxLabel { get; set; } = "TAX";

    public decimal TaxRatePercent { get; set; } = 0m;

    public decimal DefaultOpeningFloat { get; set; } = 50.00m;

    public decimal UsbRatePerGb { get; set; } = 1.00m;

    public decimal PrintCostPerPage { get; set; } = 0.10m;

    public string ClosingTime { get; set; } = "02:00";

    public string? LicenseKey { get; set; }

    public string? AutoBackupPath { get; set; }

    public int AutoBackupIntervalHours { get; set; } = 24;

    public DateTime? LastBackupAtUtc { get; set; }

    public bool IsConfigured { get; set; } = true;

    // Hardware Monitoring & Integrity
    public bool EnableHardwareAntiTheftWatchdog { get; set; } = true;
    public bool EnforceNativeRefreshRate { get; set; } = true;
    public bool EnableRebootOnSessionEnd { get; set; } = false;
    public string DisklessProvider { get; set; } = "Auto";
    public int OfflineGracePeriodSeconds { get; set; } = 180;
}

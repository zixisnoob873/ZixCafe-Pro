namespace ZixCafe.Domain.Entities;

public class MasterSystemSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string SchemaVersion { get; set; } = "1.0.0";

    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public string LastUpdatedBy { get; set; } = "System";

    // 1. Rack & Terminal Policies
    public int InactivityStandbyMinutes { get; set; } = 10;
    public bool EnableInactivityStandby { get; set; } = true;
    public string InactivityStandbyMode { get; set; } = "Sleep"; // Sleep, Hibernate, Shutdown
    public string ProhibitedProcessesCsv { get; set; } = "cheatengine,cheatengine-x86_64,artmoney,speedhack,wireshark,processhacker";
    public bool AutoKillProhibitedProcesses { get; set; } = true;
    public string UsbStoragePolicy { get; set; } = "WhitelistHidOnly"; // AllowAll, WhitelistHidOnly, BlockMassStorage
    public bool EnforceNativeDisplayRefreshRate { get; set; } = true;
    public int TargetRefreshRateHz { get; set; } = 240;
    public bool ShellLockBlockWinKey { get; set; } = true;
    public bool ShellLockBlockAltTab { get; set; } = true;
    public bool ShellLockBlockCtrlShiftEsc { get; set; } = true;
    public bool ShellLockBlockTaskManager { get; set; } = true;

    // 2. Session Lifecycle & Privacy Scrubbers
    public bool CleanupKillUserProcessesOnSessionEnd { get; set; } = true;
    public bool CleanupClearBrowserCachesOnSessionEnd { get; set; } = true;
    public bool CleanupWipeDownloadsAndDesktop { get; set; } = true;
    public bool CleanupResetMasterVolume { get; set; } = true;
    public int CleanupDefaultMasterVolumePercent { get; set; } = 50;
    public bool CleanupResetMouseSensitivity { get; set; } = true;
    public int NetworkDropGracePeriodSeconds { get; set; } = 180;
    public int SessionExtensionWarningMinutes { get; set; } = 5;
    public bool EnableRebootToRestoreOnSessionEnd { get; set; } = false;
    public string DisklessProvider { get; set; } = "None"; // None, DeepFreeze, ShadowDefender, CCBoot, iCafeCloud

    // 3. Dynamic Tariff & Billing Engine
    public decimal MinimumSessionCharge { get; set; } = 1.00m;
    public string CurrencyRoundingRule { get; set; } = "None"; // None, NearestOne, NearestFive, NearestTen
    public bool EnableFixedWindowPasses { get; set; } = true;
    public bool EnableDynamicOccupancyMultipliers { get; set; } = false;
    public decimal LowOccupancyDiscountPercent { get; set; } = 10m; // When < 30% occupancy
    public decimal HighOccupancySurchargePercent { get; set; } = 15m; // When > 85% occupancy
    public int OccupancyLowThresholdPercent { get; set; } = 30;
    public int OccupancyHighThresholdPercent { get; set; } = 85;

    // 4. Retail POS, Kitchen & Receipts
    public string VenueName { get; set; } = "ZixCafe Arena";
    public string CurrencyCode { get; set; } = "USD";
    public string CurrencySymbol { get; set; } = "$";
    public string Locale { get; set; } = "en-US";
    public int CurrencyDecimalPlaces { get; set; } = 2;
    public string TaxLabel { get; set; } = "TAX";
    public decimal TaxRatePercent { get; set; } = 0.00m;
    public decimal DefaultOpeningFloat { get; set; } = 50.00m;
    public string ReceiptHeaderText { get; set; } = "ZIXCAFE PRO ESPORTS LOUNGE\nHigh-Performance Gaming & VR";
    public string ReceiptFooterNotes { get; set; } = "Thank you for gaming with us!\nVisit again soon. zixcafe.gg";
    public string ReceiptLogoPath { get; set; } = "";
    public int ReceiptPrinterWidthMm { get; set; } = 80; // 58, 80
    public string CashDrawerKickPulseCode { get; set; } = "27,112,0,25,250"; // Standard ESC/POS pulse
    public bool EnforceMandatoryHardwareLoanReturnOnCheckout { get; set; } = true;

    // 5. RBAC & Staff Governance
    public bool RequireSupervisorPinForManualTimeAdd { get; set; } = true;
    public bool RequireSupervisorPinForBillVoid { get; set; } = true;
    public bool RequireSupervisorPinForManualDrawerKick { get; set; } = true;
    public bool RequireSupervisorPinForStockAdjustment { get; set; } = true;
    public bool EnforceBlindCashDrawerClose { get; set; } = false; // Hide expected cash total from cashier during Z-Report count

    // 6. Network, Router & Energy / IoT Integration
    public int SignalRServerPort { get; set; } = 40000;
    public string WebDashboardBindUrl { get; set; } = "http://*:40000/dashboard";
    public string RouterType { get; set; } = "None"; // None, MikroTik, OpenWrt, Generic
    public string RouterIpAddress { get; set; } = "192.168.1.1";
    public int RouterApiPort { get; set; } = 8728;
    public string RouterUsername { get; set; } = "admin";
    public string? RouterEncryptedPassword { get; set; }
    public int GuestDefaultBandwidthLimitMbps { get; set; } = 50;
    public int WakeOnLanPort { get; set; } = 9;
    public string WakeOnLanBroadcastSubnet { get; set; } = "255.255.255.255";
    public string MqttBrokerAddress { get; set; } = "localhost";
    public int MqttBrokerPort { get; set; } = 1883;
    public string MqttUsername { get; set; } = "";
    public string? MqttPassword { get; set; }
    public string DatabaseAutoBackupPath { get; set; } = "backups";
    public int DatabaseAutoBackupIntervalHours { get; set; } = 24;
    public int DatabaseBackupRetentionCount { get; set; } = 30;

    public static MasterSystemSettings CreateDefault() => new();
}

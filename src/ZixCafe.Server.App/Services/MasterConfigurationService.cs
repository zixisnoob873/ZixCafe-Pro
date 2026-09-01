using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Text.Json;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Services;

public class MasterConfigurationService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminalHub;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboardHub;
    private MasterSystemSettings? _cachedSettings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MasterConfigurationService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<TerminalHub, ITerminalClient> terminalHub,
        IHubContext<DashboardHub, IDashboardClient> dashboardHub)
    {
        _dbFactory = dbFactory;
        _terminalHub = terminalHub;
        _dashboardHub = dashboardHub;
    }

    public async Task<MasterSystemSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        if (_cachedSettings is not null) return _cachedSettings;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedSettings is not null) return _cachedSettings;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var settings = await db.SystemSettings.FirstOrDefaultAsync(ct);
            if (settings is null)
            {
                settings = MasterSystemSettings.CreateDefault();
                db.SystemSettings.Add(settings);
                await db.SaveChangesAsync(ct);
            }

            _cachedSettings = settings;
            return settings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MasterSystemSettingsDto> GetSettingsDtoAsync(CancellationToken ct = default)
    {
        var s = await GetSettingsAsync(ct);
        return MapToDto(s);
    }

    public async Task<ResultResponse> SaveSettingsDtoAsync(
        MasterSystemSettingsDto dto,
        string reason,
        string cashierName,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var existing = await db.SystemSettings.FirstOrDefaultAsync(ct);
            if (existing is null)
            {
                existing = MasterSystemSettings.CreateDefault();
                db.SystemSettings.Add(existing);
            }

            MapFromDto(dto, existing);
            existing.LastUpdatedAtUtc = DateTime.UtcNow;
            existing.LastUpdatedBy = string.IsNullOrWhiteSpace(cashierName) ? "Admin" : cashierName;

            // Cryptographic audit log entry
            await db.AppendAuditAsync(
                "system.config_update",
                "SystemConfig",
                existing.Id.ToString(),
                $"Updated system configuration: {reason}. Schema: {existing.SchemaVersion}",
                existing.LastUpdatedBy,
                ct);

            await db.SaveChangesAsync(ct);
            _cachedSettings = existing;

            // Broadcast real-time update to Studio and Terminal Agents
            var updatedDto = MapToDto(existing);
            await _dashboardHub.Clients.All.OnConfigurationUpdated(updatedDto);
            await _terminalHub.Clients.All.ApplyRuntimePolicy(updatedDto);

            return ResultResponse.Success("Configuration saved and broadcast successfully.");
        }
        catch (Exception ex)
        {
            return ResultResponse.Fail($"Failed to save configuration: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MasterSystemSettingsDto> ResetCategoryAsync(
        string category,
        string cashierName,
        CancellationToken ct = default)
    {
        var current = await GetSettingsAsync(ct);
        var defaults = MasterSystemSettings.CreateDefault();

        switch (category.ToLowerInvariant())
        {
            case "rack":
            case "terminal":
            case "rackpolicies":
                current.InactivityStandbyMinutes = defaults.InactivityStandbyMinutes;
                current.EnableInactivityStandby = defaults.EnableInactivityStandby;
                current.InactivityStandbyMode = defaults.InactivityStandbyMode;
                current.ProhibitedProcessesCsv = defaults.ProhibitedProcessesCsv;
                current.AutoKillProhibitedProcesses = defaults.AutoKillProhibitedProcesses;
                current.UsbStoragePolicy = defaults.UsbStoragePolicy;
                current.EnforceNativeDisplayRefreshRate = defaults.EnforceNativeDisplayRefreshRate;
                current.TargetRefreshRateHz = defaults.TargetRefreshRateHz;
                current.ShellLockBlockWinKey = defaults.ShellLockBlockWinKey;
                current.ShellLockBlockAltTab = defaults.ShellLockBlockAltTab;
                current.ShellLockBlockCtrlShiftEsc = defaults.ShellLockBlockCtrlShiftEsc;
                current.ShellLockBlockTaskManager = defaults.ShellLockBlockTaskManager;
                break;

            case "privacy":
            case "session":
            case "cleaner":
                current.CleanupKillUserProcessesOnSessionEnd = defaults.CleanupKillUserProcessesOnSessionEnd;
                current.CleanupClearBrowserCachesOnSessionEnd = defaults.CleanupClearBrowserCachesOnSessionEnd;
                current.CleanupWipeDownloadsAndDesktop = defaults.CleanupWipeDownloadsAndDesktop;
                current.CleanupResetMasterVolume = defaults.CleanupResetMasterVolume;
                current.CleanupDefaultMasterVolumePercent = defaults.CleanupDefaultMasterVolumePercent;
                current.CleanupResetMouseSensitivity = defaults.CleanupResetMouseSensitivity;
                current.NetworkDropGracePeriodSeconds = defaults.NetworkDropGracePeriodSeconds;
                current.SessionExtensionWarningMinutes = defaults.SessionExtensionWarningMinutes;
                current.EnableRebootToRestoreOnSessionEnd = defaults.EnableRebootToRestoreOnSessionEnd;
                current.DisklessProvider = defaults.DisklessProvider;
                break;

            case "tariff":
            case "billing":
                current.MinimumSessionCharge = defaults.MinimumSessionCharge;
                current.CurrencyRoundingRule = defaults.CurrencyRoundingRule;
                current.EnableFixedWindowPasses = defaults.EnableFixedWindowPasses;
                current.EnableDynamicOccupancyMultipliers = defaults.EnableDynamicOccupancyMultipliers;
                current.LowOccupancyDiscountPercent = defaults.LowOccupancyDiscountPercent;
                current.HighOccupancySurchargePercent = defaults.HighOccupancySurchargePercent;
                current.OccupancyLowThresholdPercent = defaults.OccupancyLowThresholdPercent;
                current.OccupancyHighThresholdPercent = defaults.OccupancyHighThresholdPercent;
                break;

            case "pos":
            case "receipt":
            case "kitchen":
                current.VenueName = defaults.VenueName;
                current.CurrencyCode = defaults.CurrencyCode;
                current.CurrencySymbol = defaults.CurrencySymbol;
                current.Locale = defaults.Locale;
                current.CurrencyDecimalPlaces = defaults.CurrencyDecimalPlaces;
                current.TaxLabel = defaults.TaxLabel;
                current.TaxRatePercent = defaults.TaxRatePercent;
                current.DefaultOpeningFloat = defaults.DefaultOpeningFloat;
                current.ReceiptHeaderText = defaults.ReceiptHeaderText;
                current.ReceiptFooterNotes = defaults.ReceiptFooterNotes;
                current.ReceiptLogoPath = defaults.ReceiptLogoPath;
                current.ReceiptPrinterWidthMm = defaults.ReceiptPrinterWidthMm;
                current.CashDrawerKickPulseCode = defaults.CashDrawerKickPulseCode;
                current.EnforceMandatoryHardwareLoanReturnOnCheckout = defaults.EnforceMandatoryHardwareLoanReturnOnCheckout;
                break;

            case "rbac":
            case "governance":
                current.RequireSupervisorPinForManualTimeAdd = defaults.RequireSupervisorPinForManualTimeAdd;
                current.RequireSupervisorPinForBillVoid = defaults.RequireSupervisorPinForBillVoid;
                current.RequireSupervisorPinForManualDrawerKick = defaults.RequireSupervisorPinForManualDrawerKick;
                current.RequireSupervisorPinForStockAdjustment = defaults.RequireSupervisorPinForStockAdjustment;
                current.EnforceBlindCashDrawerClose = defaults.EnforceBlindCashDrawerClose;
                break;

            case "network":
            case "energy":
            case "iot":
            case "router":
                current.SignalRServerPort = defaults.SignalRServerPort;
                current.WebDashboardBindUrl = defaults.WebDashboardBindUrl;
                current.RouterType = defaults.RouterType;
                current.RouterIpAddress = defaults.RouterIpAddress;
                current.RouterApiPort = defaults.RouterApiPort;
                current.RouterUsername = defaults.RouterUsername;
                current.GuestDefaultBandwidthLimitMbps = defaults.GuestDefaultBandwidthLimitMbps;
                current.WakeOnLanPort = defaults.WakeOnLanPort;
                current.WakeOnLanBroadcastSubnet = defaults.WakeOnLanBroadcastSubnet;
                current.MqttBrokerAddress = defaults.MqttBrokerAddress;
                current.MqttBrokerPort = defaults.MqttBrokerPort;
                current.MqttUsername = defaults.MqttUsername;
                current.DatabaseAutoBackupPath = defaults.DatabaseAutoBackupPath;
                current.DatabaseAutoBackupIntervalHours = defaults.DatabaseAutoBackupIntervalHours;
                current.DatabaseBackupRetentionCount = defaults.DatabaseBackupRetentionCount;
                break;

            case "all":
            case "global":
                defaults.Id = current.Id;
                MapFromDto(MapToDto(defaults), current);
                break;
        }

        var dto = MapToDto(current);
        await SaveSettingsDtoAsync(dto, $"Reset category '{category}' to defaults", cashierName, ct);
        return dto;
    }

    public static MasterSystemSettingsDto MapToDto(MasterSystemSettings s) => new(
        s.SchemaVersion,
        s.LastUpdatedAtUtc,
        s.LastUpdatedBy,
        s.InactivityStandbyMinutes,
        s.EnableInactivityStandby,
        s.InactivityStandbyMode,
        s.ProhibitedProcessesCsv,
        s.AutoKillProhibitedProcesses,
        s.UsbStoragePolicy,
        s.EnforceNativeDisplayRefreshRate,
        s.TargetRefreshRateHz,
        s.ShellLockBlockWinKey,
        s.ShellLockBlockAltTab,
        s.ShellLockBlockCtrlShiftEsc,
        s.ShellLockBlockTaskManager,
        s.CleanupKillUserProcessesOnSessionEnd,
        s.CleanupClearBrowserCachesOnSessionEnd,
        s.CleanupWipeDownloadsAndDesktop,
        s.CleanupResetMasterVolume,
        s.CleanupDefaultMasterVolumePercent,
        s.CleanupResetMouseSensitivity,
        s.NetworkDropGracePeriodSeconds,
        s.SessionExtensionWarningMinutes,
        s.EnableRebootToRestoreOnSessionEnd,
        s.DisklessProvider,
        s.MinimumSessionCharge,
        s.CurrencyRoundingRule,
        s.EnableFixedWindowPasses,
        s.EnableDynamicOccupancyMultipliers,
        s.LowOccupancyDiscountPercent,
        s.HighOccupancySurchargePercent,
        s.OccupancyLowThresholdPercent,
        s.OccupancyHighThresholdPercent,
        s.VenueName,
        s.CurrencyCode,
        s.CurrencySymbol,
        s.Locale,
        s.CurrencyDecimalPlaces,
        s.TaxLabel,
        s.TaxRatePercent,
        s.DefaultOpeningFloat,
        s.ReceiptHeaderText,
        s.ReceiptFooterNotes,
        s.ReceiptLogoPath,
        s.ReceiptPrinterWidthMm,
        s.CashDrawerKickPulseCode,
        s.EnforceMandatoryHardwareLoanReturnOnCheckout,
        s.RequireSupervisorPinForManualTimeAdd,
        s.RequireSupervisorPinForBillVoid,
        s.RequireSupervisorPinForManualDrawerKick,
        s.RequireSupervisorPinForStockAdjustment,
        s.EnforceBlindCashDrawerClose,
        s.SignalRServerPort,
        s.WebDashboardBindUrl,
        s.RouterType,
        s.RouterIpAddress,
        s.RouterApiPort,
        s.RouterUsername,
        s.GuestDefaultBandwidthLimitMbps,
        s.WakeOnLanPort,
        s.WakeOnLanBroadcastSubnet,
        s.MqttBrokerAddress,
        s.MqttBrokerPort,
        s.MqttUsername,
        s.DatabaseAutoBackupPath,
        s.DatabaseAutoBackupIntervalHours,
        s.DatabaseBackupRetentionCount);

    public static void MapFromDto(MasterSystemSettingsDto dto, MasterSystemSettings s)
    {
        s.InactivityStandbyMinutes = Math.Clamp(dto.InactivityStandbyMinutes, 1, 1440);
        s.EnableInactivityStandby = dto.EnableInactivityStandby;
        s.InactivityStandbyMode = dto.InactivityStandbyMode;
        s.ProhibitedProcessesCsv = dto.ProhibitedProcessesCsv;
        s.AutoKillProhibitedProcesses = dto.AutoKillProhibitedProcesses;
        s.UsbStoragePolicy = dto.UsbStoragePolicy;
        s.EnforceNativeDisplayRefreshRate = dto.EnforceNativeDisplayRefreshRate;
        s.TargetRefreshRateHz = dto.TargetRefreshRateHz;
        s.ShellLockBlockWinKey = dto.ShellLockBlockWinKey;
        s.ShellLockBlockAltTab = dto.ShellLockBlockAltTab;
        s.ShellLockBlockCtrlShiftEsc = dto.ShellLockBlockCtrlShiftEsc;
        s.ShellLockBlockTaskManager = dto.ShellLockBlockTaskManager;

        s.CleanupKillUserProcessesOnSessionEnd = dto.CleanupKillUserProcessesOnSessionEnd;
        s.CleanupClearBrowserCachesOnSessionEnd = dto.CleanupClearBrowserCachesOnSessionEnd;
        s.CleanupWipeDownloadsAndDesktop = dto.CleanupWipeDownloadsAndDesktop;
        s.CleanupResetMasterVolume = dto.CleanupResetMasterVolume;
        s.CleanupDefaultMasterVolumePercent = Math.Clamp(dto.CleanupDefaultMasterVolumePercent, 0, 100);
        s.CleanupResetMouseSensitivity = dto.CleanupResetMouseSensitivity;
        s.NetworkDropGracePeriodSeconds = Math.Clamp(dto.NetworkDropGracePeriodSeconds, 10, 3600);
        s.SessionExtensionWarningMinutes = Math.Clamp(dto.SessionExtensionWarningMinutes, 1, 60);
        s.EnableRebootToRestoreOnSessionEnd = dto.EnableRebootToRestoreOnSessionEnd;
        s.DisklessProvider = dto.DisklessProvider;

        s.MinimumSessionCharge = Math.Max(0m, dto.MinimumSessionCharge);
        s.CurrencyRoundingRule = dto.CurrencyRoundingRule;
        s.EnableFixedWindowPasses = dto.EnableFixedWindowPasses;
        s.EnableDynamicOccupancyMultipliers = dto.EnableDynamicOccupancyMultipliers;
        s.LowOccupancyDiscountPercent = Math.Clamp(dto.LowOccupancyDiscountPercent, 0m, 90m);
        s.HighOccupancySurchargePercent = Math.Clamp(dto.HighOccupancySurchargePercent, 0m, 100m);
        s.OccupancyLowThresholdPercent = Math.Clamp(dto.OccupancyLowThresholdPercent, 5, 50);
        s.OccupancyHighThresholdPercent = Math.Clamp(dto.OccupancyHighThresholdPercent, 50, 95);

        s.VenueName = string.IsNullOrWhiteSpace(dto.VenueName) ? "ZixCafe Arena" : dto.VenueName.Trim();
        s.CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? "USD" : dto.CurrencyCode.Trim();
        s.CurrencySymbol = string.IsNullOrWhiteSpace(dto.CurrencySymbol) ? "$" : dto.CurrencySymbol.Trim();
        s.Locale = string.IsNullOrWhiteSpace(dto.Locale) ? "en-US" : dto.Locale.Trim();
        s.CurrencyDecimalPlaces = Math.Clamp(dto.CurrencyDecimalPlaces, 0, 4);
        s.TaxLabel = string.IsNullOrWhiteSpace(dto.TaxLabel) ? "TAX" : dto.TaxLabel.Trim();
        s.TaxRatePercent = Math.Clamp(dto.TaxRatePercent, 0m, 100m);
        s.DefaultOpeningFloat = Math.Max(0m, dto.DefaultOpeningFloat);
        s.ReceiptHeaderText = dto.ReceiptHeaderText ?? "";
        s.ReceiptFooterNotes = dto.ReceiptFooterNotes ?? "";
        s.ReceiptLogoPath = dto.ReceiptLogoPath ?? "";
        s.ReceiptPrinterWidthMm = dto.ReceiptPrinterWidthMm == 58 ? 58 : 80;
        s.CashDrawerKickPulseCode = dto.CashDrawerKickPulseCode ?? "27,112,0,25,250";
        s.EnforceMandatoryHardwareLoanReturnOnCheckout = dto.EnforceMandatoryHardwareLoanReturnOnCheckout;

        s.RequireSupervisorPinForManualTimeAdd = dto.RequireSupervisorPinForManualTimeAdd;
        s.RequireSupervisorPinForBillVoid = dto.RequireSupervisorPinForBillVoid;
        s.RequireSupervisorPinForManualDrawerKick = dto.RequireSupervisorPinForManualDrawerKick;
        s.RequireSupervisorPinForStockAdjustment = dto.RequireSupervisorPinForStockAdjustment;
        s.EnforceBlindCashDrawerClose = dto.EnforceBlindCashDrawerClose;

        s.SignalRServerPort = dto.SignalRServerPort > 0 ? dto.SignalRServerPort : 40000;
        s.WebDashboardBindUrl = dto.WebDashboardBindUrl ?? "http://*:40000/dashboard";
        s.RouterType = dto.RouterType ?? "None";
        s.RouterIpAddress = dto.RouterIpAddress ?? "192.168.1.1";
        s.RouterApiPort = dto.RouterApiPort > 0 ? dto.RouterApiPort : 8728;
        s.RouterUsername = dto.RouterUsername ?? "admin";
        s.GuestDefaultBandwidthLimitMbps = Math.Clamp(dto.GuestDefaultBandwidthLimitMbps, 1, 10000);
        s.WakeOnLanPort = dto.WakeOnLanPort > 0 ? dto.WakeOnLanPort : 9;
        s.WakeOnLanBroadcastSubnet = dto.WakeOnLanBroadcastSubnet ?? "255.255.255.255";
        s.MqttBrokerAddress = dto.MqttBrokerAddress ?? "localhost";
        s.MqttBrokerPort = dto.MqttBrokerPort > 0 ? dto.MqttBrokerPort : 1883;
        s.MqttUsername = dto.MqttUsername ?? "";
        s.DatabaseAutoBackupPath = dto.DatabaseAutoBackupPath ?? "backups";
        s.DatabaseAutoBackupIntervalHours = Math.Clamp(dto.DatabaseAutoBackupIntervalHours, 1, 720);
        s.DatabaseBackupRetentionCount = Math.Clamp(dto.DatabaseBackupRetentionCount, 1, 365);
    }
}

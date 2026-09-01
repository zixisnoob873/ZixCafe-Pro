using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Services;

public class HardwareIntegrityService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly AlertsCenterService _alertsCenter;
    private readonly VenueSettingsService _venueSettings;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminalHub;
    private readonly Dictionary<Guid, HardwareInventoryDto> _latestReports = new();

    public HardwareIntegrityService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        AlertsCenterService alertsCenter,
        VenueSettingsService venueSettings,
        IHubContext<TerminalHub, ITerminalClient> terminalHub)
    {
        _dbFactory = dbFactory;
        _alertsCenter = alertsCenter;
        _venueSettings = venueSettings;
        _terminalHub = terminalHub;
    }

    public async Task ProcessHardwareInventoryAsync(HardwareInventoryDto inventory)
    {
        _latestReports[inventory.TerminalId] = inventory;

        var settings = await _venueSettings.GetSettingsAsync();
        if (!settings.EnableHardwareAntiTheftWatchdog)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FindAsync(inventory.TerminalId);
        if (terminal is null) return;

        var baseline = await db.HardwareBaselines.FirstOrDefaultAsync(h => h.TerminalId == inventory.TerminalId);

        var currentUsbIds = inventory.UsbDevices.Where(u => u.IsConnected).Select(u => u.DeviceId).ToList();

        if (baseline is null)
        {
            // First time registration: establish baseline automatically
            baseline = new TerminalHardwareBaseline
            {
                TerminalId = inventory.TerminalId,
                CpuName = inventory.CpuName,
                CpuId = inventory.CpuId,
                GpuName = inventory.GpuName,
                GpuDeviceId = inventory.GpuDeviceId,
                GpuVramMb = inventory.GpuVramMb,
                TotalRamMb = inventory.TotalRamMb,
                RamSerials = inventory.RamSerials,
                DiskModel = inventory.DiskModel,
                DiskSerial = inventory.DiskSerial,
                UsbDevicesJson = JsonSerializer.Serialize(currentUsbIds),
                NativeRefreshRateHz = inventory.MaxSupportedRefreshRateHz,
                DisplayResolution = inventory.DisplayResolution,
                EstablishedAtUtc = DateTime.UtcNow,
                LastVerifiedAtUtc = DateTime.UtcNow
            };
            db.HardwareBaselines.Add(baseline);
            await db.SaveChangesAsync();
            return;
        }

        // Compare against established baseline
        var discrepancies = HardwareAntiTheftEngine.Compare(
            baseline,
            inventory.CpuName,
            inventory.CpuId,
            inventory.GpuName,
            inventory.GpuDeviceId,
            inventory.TotalRamMb,
            inventory.RamSerials,
            inventory.DiskSerial,
            currentUsbIds);

        foreach (var disc in discrepancies)
        {
            var msg = $"[Hardware Watchdog] {terminal.Name}: {disc.Description}";
            await _alertsCenter.RaiseAlertAsync(disc.Severity, "hardware.theft_warning", msg, terminal.Id, "System");
            await AppendAuditAsync(db, "security.hardware_discrepancy", terminal.Id.ToString(), JsonSerializer.Serialize(disc), "System");
        }

        baseline.LastVerifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<HardwareBaselineDto?> GetTerminalHardwareAsync(Guid terminalId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var baseline = await db.HardwareBaselines
            .Include(h => h.Terminal)
            .FirstOrDefaultAsync(h => h.TerminalId == terminalId);

        if (baseline is null)
        {
            if (_latestReports.TryGetValue(terminalId, out var live))
            {
                return new HardwareBaselineDto(
                    Guid.Empty,
                    terminalId,
                    "Terminal",
                    live.CpuName,
                    live.CpuId,
                    live.GpuName,
                    live.GpuDeviceId,
                    live.TotalRamMb,
                    live.RamSerials,
                    live.DiskModel,
                    live.DiskSerial,
                    live.MaxSupportedRefreshRateHz,
                    live.DisplayResolution,
                    live.UsbDevices.Select(u => u.Name).ToList(),
                    live.CapturedAtUtc,
                    live.CapturedAtUtc);
            }
            return null;
        }

        var usbList = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(baseline.UsbDevicesJson))
            {
                usbList = JsonSerializer.Deserialize<List<string>>(baseline.UsbDevicesJson) ?? [];
            }
        }
        catch { }

        return new HardwareBaselineDto(
            baseline.Id,
            baseline.TerminalId,
            baseline.Terminal?.Name ?? "Terminal",
            baseline.CpuName,
            baseline.CpuId,
            baseline.GpuName,
            baseline.GpuDeviceId,
            baseline.TotalRamMb,
            baseline.RamSerials,
            baseline.DiskModel,
            baseline.DiskSerial,
            baseline.NativeRefreshRateHz,
            baseline.DisplayResolution,
            usbList,
            baseline.EstablishedAtUtc,
            baseline.LastVerifiedAtUtc);
    }

    public async Task<ResultResponse> SetTerminalHardwareBaselineAsync(Guid terminalId, string requestingCashier)
    {
        if (!_latestReports.TryGetValue(terminalId, out var live))
        {
            return new ResultResponse(false, "No hardware report received from this terminal yet.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var baseline = await db.HardwareBaselines.FirstOrDefaultAsync(h => h.TerminalId == terminalId);

        var currentUsbIds = live.UsbDevices.Where(u => u.IsConnected).Select(u => u.DeviceId).ToList();

        if (baseline is null)
        {
            baseline = new TerminalHardwareBaseline
            {
                TerminalId = terminalId,
                CpuName = live.CpuName,
                CpuId = live.CpuId,
                GpuName = live.GpuName,
                GpuDeviceId = live.GpuDeviceId,
                GpuVramMb = live.GpuVramMb,
                TotalRamMb = live.TotalRamMb,
                RamSerials = live.RamSerials,
                DiskModel = live.DiskModel,
                DiskSerial = live.DiskSerial,
                UsbDevicesJson = JsonSerializer.Serialize(currentUsbIds),
                NativeRefreshRateHz = live.MaxSupportedRefreshRateHz,
                DisplayResolution = live.DisplayResolution,
                EstablishedAtUtc = DateTime.UtcNow,
                LastVerifiedAtUtc = DateTime.UtcNow
            };
            db.HardwareBaselines.Add(baseline);
        }
        else
        {
            baseline.CpuName = live.CpuName;
            baseline.CpuId = live.CpuId;
            baseline.GpuName = live.GpuName;
            baseline.GpuDeviceId = live.GpuDeviceId;
            baseline.GpuVramMb = live.GpuVramMb;
            baseline.TotalRamMb = live.TotalRamMb;
            baseline.RamSerials = live.RamSerials;
            baseline.DiskModel = live.DiskModel;
            baseline.DiskSerial = live.DiskSerial;
            baseline.UsbDevicesJson = JsonSerializer.Serialize(currentUsbIds);
            baseline.NativeRefreshRateHz = live.MaxSupportedRefreshRateHz;
            baseline.DisplayResolution = live.DisplayResolution;
            baseline.EstablishedAtUtc = DateTime.UtcNow;
            baseline.LastVerifiedAtUtc = DateTime.UtcNow;
        }

        await AppendAuditAsync(db, "hardware.set_baseline", terminalId.ToString(), $"CPU={live.CpuName};GPU={live.GpuName};RAM={live.TotalRamMb}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> EnforceTerminalRefreshRateAsync(Guid terminalId, string requestingCashier)
    {
        var baseline = await GetTerminalHardwareAsync(terminalId);
        var targetHz = baseline?.NativeRefreshRateHz ?? 240;

        await _terminalHub.Clients.Group($"terminal_{terminalId}").EnforceDisplayRefreshRate(targetHz);
        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> TriggerDisklessWipeAsync(Guid terminalId, string requestingCashier)
    {
        var settings = await _venueSettings.GetSettingsAsync();
        var provider = settings.DisklessProvider;

        await _terminalHub.Clients.Group($"terminal_{terminalId}").CoordinateDisklessWipe(provider, true);
        return new ResultResponse(true, null);
    }

    private static async Task AppendAuditAsync(ZixCafeDbContext db, string action, string? targetId, string? detail, string cashier)
    {
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "Terminal", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "Terminal",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
    }
}

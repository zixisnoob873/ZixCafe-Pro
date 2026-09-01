using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Services;

public class EnergyAndIoTHostService : BackgroundService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminalHub;
    private readonly MasterConfigurationService _configService;
    private readonly TerminalRegistry _registry;
    private readonly ILogger<EnergyAndIoTHostService> _logger;

    public EnergyAndIoTHostService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<TerminalHub, ITerminalClient> terminalHub,
        MasterConfigurationService configService,
        TerminalRegistry registry,
        ILogger<EnergyAndIoTHostService> logger)
    {
        _dbFactory = dbFactory;
        _terminalHub = terminalHub;
        _configService = configService;
        _registry = registry;
        _logger = logger;
    }

    public async Task<ResultResponse> WakeTerminalAsync(Guid terminalId, string requestingCashier, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var terminal = await db.Terminals.FindAsync([terminalId], ct);
        if (terminal is null)
        {
            return ResultResponse.Fail("Terminal not found.");
        }

        if (string.IsNullOrWhiteSpace(terminal.MacAddress))
        {
            return ResultResponse.Fail($"Terminal '{terminal.Name}' does not have a MAC address configured for Wake-on-LAN.");
        }

        var settings = await _configService.GetSettingsAsync(ct);
        var success = await WakeOnLanService.SendMagicPacketAsync(
            terminal.MacAddress,
            settings.WakeOnLanBroadcastSubnet,
            settings.WakeOnLanPort);

        if (success)
        {
            await db.AppendAuditAsync(
                "energy.wol_wake",
                "EnergyIoT",
                terminal.Id.ToString(),
                $"Sent Wake-on-LAN magic packet to terminal '{terminal.Name}' (MAC: {terminal.MacAddress})",
                requestingCashier,
                ct);
            await db.SaveChangesAsync(ct);

            return ResultResponse.Success($"Magic packet sent to {terminal.Name} ({terminal.MacAddress}).");
        }

        return ResultResponse.Fail($"Failed to send WoL packet to {terminal.Name}.");
    }

    public async Task<ResultResponse> WakeAllTerminalsAsync(Guid? zoneId, string requestingCashier, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.Terminals.AsQueryable();
        if (zoneId.HasValue)
        {
            query = query.Where(t => t.ZoneId == zoneId.Value);
        }

        var terminals = await query.Where(t => !string.IsNullOrEmpty(t.MacAddress)).ToListAsync(ct);
        if (terminals.Count == 0)
        {
            return ResultResponse.Fail("No terminals with configured MAC addresses found.");
        }

        var settings = await _configService.GetSettingsAsync(ct);
        var wokenCount = 0;

        foreach (var t in terminals)
        {
            if (await WakeOnLanService.SendMagicPacketAsync(t.MacAddress!, settings.WakeOnLanBroadcastSubnet, settings.WakeOnLanPort))
            {
                wokenCount++;
            }
        }

        await db.AppendAuditAsync(
            "energy.wol_wake_batch",
            "EnergyIoT",
            zoneId?.ToString() ?? "All",
            $"Sent Wake-on-LAN magic packets to {wokenCount}/{terminals.Count} terminals (Zone: {zoneId?.ToString() ?? "All"})",
            requestingCashier,
            ct);
        await db.SaveChangesAsync(ct);

        return ResultResponse.Success($"Sent WoL magic packets to {wokenCount} terminals.");
    }

    public async Task<ResultResponse> TriggerSmartRelayAsync(Guid terminalId, bool powerOn, string cashierName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var terminal = await db.Terminals.FindAsync([terminalId], ct);
        if (terminal is null)
        {
            return ResultResponse.Fail("Terminal not found.");
        }

        if (string.IsNullOrWhiteSpace(terminal.RelayAddress) || terminal.RelayType == "None")
        {
            return ResultResponse.Fail($"Terminal '{terminal.Name}' has no Smart Relay configured.");
        }

        var cmd = new SmartRelayCommand(terminal.RelayType ?? "Shelly", terminal.RelayAddress, terminal.RelayChannel, powerOn);
        var success = await SmartRelayController.SendPowerCommandAsync(cmd);

        await db.AppendAuditAsync(
            "energy.smart_relay_toggle",
            "EnergyIoT",
            terminal.Id.ToString(),
            $"Toggled smart relay power to {(powerOn ? "ON" : "OFF")} for '{terminal.Name}' ({terminal.RelayType} @ {terminal.RelayAddress})",
            cashierName,
            ct);
        await db.SaveChangesAsync(ct);

        return success
            ? ResultResponse.Success($"Power for '{terminal.Name}' set to {(powerOn ? "ON" : "OFF")}.")
            : ResultResponse.Fail($"Failed to communicate with relay device at {terminal.RelayAddress}.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await CheckInactivityStandbyAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in Energy and IoT watchdog loop");
            }
        }
    }

    private async Task CheckInactivityStandbyAsync(CancellationToken ct)
    {
        var settings = await _configService.GetSettingsAsync(ct);
        if (!settings.EnableInactivityStandby || settings.InactivityStandbyMinutes <= 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var thresholdTime = DateTime.UtcNow.AddMinutes(-settings.InactivityStandbyMinutes);

        // Find available/unrented terminals that are currently online and have had no active session for >= standby minutes
        var activeSessionTerminalIds = await db.Sessions
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .Select(s => s.TerminalId)
            .Distinct()
            .ToListAsync(ct);

        var idleTerminals = await db.Terminals
            .Where(t => !activeSessionTerminalIds.Contains(t.Id) && t.Status == TerminalStatus.Available)
            .ToListAsync(ct);

        foreach (var t in idleTerminals)
        {
            // Check if connection is registered and active
            var connId = _registry.GetConnectionId(t.Id);
            if (!string.IsNullOrEmpty(connId))
            {
                _logger.LogInformation("Triggering inactivity standby ({Mode}) for idle terminal {Name}", settings.InactivityStandbyMode, t.Name);
                await _terminalHub.Clients.Client(connId).TriggerStandby(settings.InactivityStandbyMode);
            }
        }
    }
}

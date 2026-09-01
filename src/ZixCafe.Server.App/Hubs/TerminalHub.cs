using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Hubs;

public class TerminalHub : Hub<ITerminalClient>, ITerminalServer
{
    private readonly TerminalRegistry _registry;
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;
    private readonly ChatHistoryService _chatHistory;
    private readonly AlertsCenterService _alerts;
    private readonly PeripheralMeteringService _peripherals;
    private readonly RemoteOpsService _remoteOps;
    private readonly HardwareIntegrityService _hardware;
    private readonly VenueSettingsService _venueSettings;

    public TerminalHub(
        TerminalRegistry registry,
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<DashboardHub, IDashboardClient> dashboards,
        ChatHistoryService chatHistory,
        AlertsCenterService alerts,
        PeripheralMeteringService peripherals,
        RemoteOpsService remoteOps,
        HardwareIntegrityService hardware,
        VenueSettingsService venueSettings)
    {
        _registry = registry;
        _dbFactory = dbFactory;
        _dashboards = dashboards;
        _chatHistory = chatHistory;
        _alerts = alerts;
        _peripherals = peripherals;
        _remoteOps = remoteOps;
        _hardware = hardware;
        _venueSettings = venueSettings;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        Domain.Entities.Terminal? terminal;
        string? issuedSecret = null;

        if (_registry.TryConsumePairingCode(request.Credential, out var pairingTerminalId))
        {
            terminal = await db.Terminals
                .Include(t => t.Zone)
                .FirstOrDefaultAsync(t => t.Id == pairingTerminalId)
                ?? throw new HubException("Paired terminal no longer exists.");

            issuedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            terminal.SecretHash = Hash(issuedSecret);
        }
        else
        {
            var secretHash = Hash(request.Credential);
            terminal = await db.Terminals
                .Include(t => t.Zone)
                .FirstOrDefaultAsync(t => t.SecretHash == secretHash)
                ?? throw new HubException("Not paired. Ask the front desk for a pairing code.");
        }

        terminal.MachineGuid = request.MachineGuid;
        terminal.AgentVersion = request.AgentVersion;
        terminal.IpAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        terminal.LastSeenAt = DateTime.UtcNow;
        if (terminal.Status == TerminalStatus.Offline)
        {
            terminal.Status = TerminalStatus.Available;
        }
        terminal.IsLocked = true;
        await db.SaveChangesAsync();

        Context.Items["terminalId"] = terminal.Id;
        _registry.Register(new TerminalConnection
        {
            TerminalId = terminal.Id,
            Name = terminal.Name,
            ZoneName = terminal.Zone.Name,
            ConnectionId = Context.ConnectionId,
            AgentVersion = request.AgentVersion
        });

        await Groups.AddToGroupAsync(Context.ConnectionId, TerminalGroups.Terminal(terminal.Id));

        _registry.RaiseState(new TerminalStateDto(
            terminal.Id, terminal.Name, terminal.Zone.Name,
            (TerminalStatusDto)terminal.Status, true, request.AgentVersion,
            DateTime.UtcNow, null, 0, 0, null, null, false,
            terminal.MaintenanceReason, terminal.ReservedFor, terminal.CpuTemp, terminal.GpuTemp, terminal.RamPercent));

        var settings = await _venueSettings.GetSettingsAsync();
        _ = Clients.Caller.SetOfflineGracePeriod(settings.OfflineGracePeriodSeconds);

        return new RegisterResult(terminal.Id, terminal.Name, terminal.Zone.Name, issuedSecret);
    }

    public async Task HeartbeatAsync(string agentVersion, int cpuPercent, int ramPercent, int diskFreeGb)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId == Guid.Empty)
        {
            throw new HubException("Not registered.");
        }

        _registry.Touch(terminalId, Context.ConnectionId, agentVersion, cpuPercent, ramPercent, diskFreeGb);

        // Update in database
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == terminalId);
        if (terminal is not null)
        {
            terminal.LastSeenAt = DateTime.UtcNow;
            terminal.CpuTemp = cpuPercent;
            terminal.RamPercent = ramPercent;
            terminal.DiskFreeGb = diskFreeGb;
            terminal.AgentVersion = agentVersion;
            await db.SaveChangesAsync();

            // Check hardware thresholds
            if (cpuPercent >= 90)
            {
                await _alerts.RaiseAlertAsync("warn", "hardware.cpu_high",
                    $"{terminal.Name}: High CPU utilization ({cpuPercent}%).", terminalId, null);
            }
            if (diskFreeGb <= 5 && diskFreeGb >= 0)
            {
                await _alerts.RaiseAlertAsync("warn", "hardware.disk_low",
                    $"{terminal.Name}: Low disk space remaining ({diskFreeGb} GB).", terminalId, null);
            }
        }
    }

    public async Task SessionCountdownTickAsync(Guid sessionId, int minutesElapsed, decimal currentAmount)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        _registry.RaiseState(new TerminalStateDto(
            terminalId, string.Empty, string.Empty, TerminalStatusDto.InUse,
            false, null, DateTime.UtcNow, sessionId, currentAmount, minutesElapsed, null, null));
        await Task.CompletedTask;
    }

    public async Task SendChatToDeskAsync(string message)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId == Guid.Empty)
        {
            throw new HubException("Not registered.");
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var name = _registry.Get(terminalId)?.Name ?? $"Terminal {terminalId.ToString()[..8]}";
        var sentAt = DateTime.UtcNow;

        await _chatHistory.SaveChatAsync(terminalId, null, name, message, true);
        await _dashboards.Clients.Group("dashboard").ChatMessage(terminalId, name, message.Trim(), sentAt);
    }

    public async Task SubmitScreenFrameAsync(Guid requestId, byte[] jpegBytes)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId != Guid.Empty && jpegBytes.Length > 0)
        {
            await _remoteOps.RelayScreenFrameAsync(terminalId, jpegBytes);
        }
    }

    public async Task ReportProhibitedAppKilledAsync(string processName)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        var name = _registry.Get(terminalId)?.Name ?? $"Terminal {terminalId.ToString()[..8]}";
        await _alerts.RaiseAlertAsync("alert", "security.prohibited_app",
            $"{name}: Terminated prohibited application '{processName}'.", terminalId, null);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, "security.app_kill", "Terminal", terminalId.ToString(), $"process={processName}", "system", now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = "security.app_kill",
            TargetType = "Terminal",
            TargetId = terminalId.ToString(),
            DetailJson = $"process={processName}",
            CashierName = "system",
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }

    public async Task ReportUsbUsageAsync(long bytesTransferred)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId != Guid.Empty)
        {
            await _peripherals.RecordUsbTransferAsync(terminalId, bytesTransferred);
        }
    }

    public async Task ReportHardwareInventoryAsync(HardwareInventoryDto inventory)
    {
        await _hardware.ProcessHardwareInventoryAsync(inventory);
    }

    public async Task ReportDisplaySettingsAsync(int activeRefreshRateHz, int maxSupportedHz, string resolution)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId != Guid.Empty)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var terminal = await db.Terminals.FindAsync(terminalId);
            if (terminal is not null)
            {
                terminal.NativeRefreshRateHz = maxSupportedHz;
                terminal.DisplayResolution = resolution;
                await db.SaveChangesAsync();
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var terminalId = Context.Items.TryGetValue("terminalId", out var v) ? (Guid)v! : Guid.Empty;
        if (terminalId != Guid.Empty)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var terminal = await db.Terminals.Include(t => t.Zone).FirstOrDefaultAsync(t => t.Id == terminalId);
            if (terminal is not null)
            {
                terminal.Status = TerminalStatus.Offline;
                terminal.LastSeenAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                _registry.RaiseState(new TerminalStateDto(
                    terminal.Id, terminal.Name, terminal.Zone.Name,
                    TerminalStatusDto.Offline, true, terminal.AgentVersion,
                    terminal.LastSeenAt, null, 0, 0, null, null, false,
                    terminal.MaintenanceReason, terminal.ReservedFor, terminal.CpuTemp, terminal.GpuTemp, terminal.RamPercent));
            }
        }

        _registry.DropConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private static string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
}

public static class TerminalGroups
{
    public static string Terminal(Guid terminalId) => $"terminal:{terminalId}";
}

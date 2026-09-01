using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Services;

public class RemoteOpsService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;
    private readonly TerminalRegistry _registry;

    public RemoteOpsService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<TerminalHub, ITerminalClient> terminals,
        IHubContext<DashboardHub, IDashboardClient> dashboards,
        TerminalRegistry registry)
    {
        _dbFactory = dbFactory;
        _terminals = terminals;
        _dashboards = dashboards;
        _registry = registry;
    }

    public async Task<ResultResponse> RequestScreenViewAsync(Guid terminalId, string requestingCashier)
    {
        var connId = _registry.GetConnectionId(terminalId);
        if (connId is null)
        {
            return new ResultResponse(false, "Terminal is offline.");
        }

        var requestId = Guid.NewGuid();
        // Announce banner on client + request frame
        await _terminals.Clients.Client(connId).ShowBanner("info", "The front desk is viewing this screen for technical assistance.");
        await _terminals.Clients.Client(connId).CaptureScreenFrame(requestId);

        await AppendAuditAsync("remote.screen_view", terminalId.ToString(), $"requested by {requestingCashier}", requestingCashier);
        return new ResultResponse(true, null);
    }

    public event Action<Guid, byte[]>? FrameRelayed;

    public async Task RelayScreenFrameAsync(Guid terminalId, byte[] jpegBytes)
    {
        FrameRelayed?.Invoke(terminalId, jpegBytes);
        await _dashboards.Clients.All.ScreenFrameReceived(terminalId, jpegBytes);
    }

    public async Task<ResultResponse> ExecuteRemoteActionAsync(RemoteActionRequest request)
    {
        var connId = _registry.GetConnectionId(request.TerminalId);
        if (connId is null && request.Action != "wol")
        {
            return new ResultResponse(false, "Terminal is offline.");
        }

        switch (request.Action.ToLowerInvariant())
        {
            case "reboot":
                if (connId is not null)
                {
                    await _terminals.Clients.Client(connId).ShowBanner("warn", "System reboot initiated by front desk.");
                    await _terminals.Clients.Client(connId).RemoteCommand("reboot");
                }
                break;

            case "shutdown":
                if (connId is not null)
                {
                    await _terminals.Clients.Client(connId).ShowBanner("warn", "System shutdown initiated by front desk.");
                    await _terminals.Clients.Client(connId).RemoteCommand("shutdown");
                }
                break;

            case "lock":
                if (connId is not null)
                {
                    await _terminals.Clients.Client(connId).ShowLockScreen(DateTime.UtcNow);
                }
                break;

            case "wol":
                await SendWakeOnLanAsync(request.TerminalId);
                break;

            default:
                return new ResultResponse(false, $"Unknown remote action: {request.Action}");
        }

        await AppendAuditAsync($"remote.{request.Action.ToLowerInvariant()}", request.TerminalId.ToString(), request.Reason, request.CashierName);
        return new ResultResponse(true, null);
    }

    public async Task SendWakeOnLanAsync(Guid terminalId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == terminalId);
        if (terminal is null)
        {
            return;
        }

        // Best effort: broadcast standard magic packet
        try
        {
            var magicPacket = new byte[102];
            for (var i = 0; i < 6; i++) magicPacket[i] = 0xFF;
            // Dummy MAC or parsed from hardware profile if available
            var mac = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
            for (var i = 0; i < 16; i++)
            {
                Array.Copy(mac, 0, magicPacket, 6 + (i * 6), 6);
            }

            using var client = new UdpClient();
            client.EnableBroadcast = true;
            await client.SendAsync(magicPacket, magicPacket.Length, new IPEndPoint(IPAddress.Broadcast, 9));
        }
        catch
        {
        }
    }

    public async Task<IReadOnlyList<ProhibitedAppDto>> GetProhibitedAppsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var apps = await db.ProhibitedApps.OrderBy(a => a.Match).ToListAsync();
        return apps.Select(a => new ProhibitedAppDto(a.Id, a.Match, a.MatchKind, a.KillOnSight, a.IsActive)).ToList();
    }

    public async Task<ResultResponse> SaveProhibitedAppAsync(string match, string matchKind, bool killOnSight, string requestingCashier)
    {
        if (string.IsNullOrWhiteSpace(match))
        {
            return new ResultResponse(false, "Process match cannot be empty.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var app = new ProhibitedApp
        {
            Match = match.Trim(),
            MatchKind = string.IsNullOrWhiteSpace(matchKind) ? "ProcessName" : matchKind.Trim(),
            KillOnSight = killOnSight,
            IsActive = true
        };

        db.ProhibitedApps.Add(app);
        await AppendAuditAsync("prohibited_app.add", app.Id.ToString(), $"match={app.Match}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> DeleteProhibitedAppAsync(Guid id, string requestingCashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var app = await db.ProhibitedApps.FirstOrDefaultAsync(a => a.Id == id);
        if (app is null)
        {
            return new ResultResponse(false, "Prohibited application not found.");
        }

        db.ProhibitedApps.Remove(app);
        await AppendAuditAsync("prohibited_app.delete", id.ToString(), $"deleted {app.Match}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    private async Task AppendAuditAsync(string action, string? targetId, string? detail, string cashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "RemoteOps", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "RemoteOps",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
        await db.SaveChangesAsync();
    }
}

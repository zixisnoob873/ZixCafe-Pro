using System.Security.Cryptography;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Services;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZixCafe.Server.App.Hubs;

public class TerminalHub : Hub<ITerminalClient>, ITerminalServer
{
    private readonly TerminalRegistry _registry;
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;

    public TerminalHub(
        TerminalRegistry registry,
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<DashboardHub, IDashboardClient> dashboards)
    {
        _registry = registry;
        _dbFactory = dbFactory;
        _dashboards = dashboards;
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
        terminal.Status = Domain.Enums.TerminalStatus.Available;
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
            TerminalStatusDto.Available, true, request.AgentVersion,
            DateTime.UtcNow, null, 0, 0, null, null));

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
        await Task.CompletedTask;
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
        await _dashboards.Clients.Group("dashboard").ChatMessage(terminalId, name, message.Trim(), DateTime.UtcNow);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
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

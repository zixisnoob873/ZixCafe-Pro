using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ZixCafe.Server.App.Services;

/// <summary>
/// Pushes live session state (remaining minutes, running charge) to the
/// dashboard once per second, and a server-authoritative TimeSync to each
/// active terminal agent. The 1 Hz SQL read is acceptable for the skeleton;
/// Phase 2 replaces it with an in-memory active-session cache.
/// </summary>
public class SessionMonitor : BackgroundService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly TerminalRegistry _registry;
    private readonly SessionService _sessions;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;
    private static readonly TimeZoneInfo VenueTimeZone = TimeZoneInfo.Local;

    public SessionMonitor(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        TerminalRegistry registry,
        SessionService sessions,
        IHubContext<TerminalHub, ITerminalClient> terminals,
        IHubContext<DashboardHub, IDashboardClient> dashboards)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _sessions = sessions;
        _terminals = terminals;
        _dashboards = dashboards;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var actives = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Terminal).ThenInclude(t => t.Zone)
            .Include(s => s.Tariff).ThenInclude(t => t!.Rules)
            .Where(s => s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        // Auto-end prepaid/ticket/member sessions whose granted time ran out.
        // Paused sessions are exempt: their planned end shifts on resume.
        foreach (var due in actives.Where(s =>
                     s.Status == SessionStatus.Active &&
                     s.PlannedEndAt is { } end && end <= now))
        {
            var response = await _sessions.EndAsync(
                new EndSessionRequest(due.Id, "system"), "time_up");
            if (response.Ok)
            {
                await RaiseTimeUpAlertAsync(db, due.TerminalId, due.Terminal.Name, response.TotalDue);
            }
        }

        foreach (var session in actives.Where(s =>
                     !(s.PlannedEndAt is { } e && e <= now) || s.Status == SessionStatus.Paused))
        {
            // A paused session's clocks freeze at the pause moment.
            var clock = session.Status == SessionStatus.Paused && session.PausedAtUtc is { } pausedAt
                ? pausedAt
                : now;
            if (session.PlannedEndAt is { } planned && clock > planned)
            {
                clock = planned;
            }

            var elapsed = clock - session.StartedAt - TimeSpan.FromMinutes(session.PausedMinutes);
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            int? remaining = session.PlannedEndAt is { } end
                ? (int)Math.Max(0, (end - clock).TotalMinutes)
                : null;

            decimal amount = 0;
            if (session.Tariff is not null)
            {
                amount = TariffEngine.ComputeTimeCharge(
                    session.Tariff, session.StartedAt, clock, VenueTimeZone, session.PausedMinutes, out _);
            }
            amount += session.Lines.Sum(l => l.Amount);

            _registry.RaiseState(new TerminalStateDto(
                session.TerminalId,
                session.Terminal.Name,
                session.Terminal.Zone.Name,
                TerminalStatusDto.InUse,
                false,
                session.Terminal.AgentVersion,
                now,
                session.Id,
                amount,
                (int)elapsed.TotalMinutes,
                remaining,
                session.PlannedEndAt,
                session.Status == SessionStatus.Paused,
                session.Terminal.MaintenanceReason,
                session.Terminal.ReservedFor,
                session.Terminal.CpuTemp,
                session.Terminal.GpuTemp,
                session.Terminal.RamPercent));

            await _terminals.Clients.Group(TerminalGroups.Terminal(session.TerminalId)).TimeSync(
                now, session.PlannedEndAt, amount);
        }
    }

    private async Task RaiseTimeUpAlertAsync(ZixCafeDbContext db, Guid terminalId, string terminalName, decimal totalDue)
    {
        var alert = new AlertEvent
        {
            Kind = "session.time_up",
            Severity = AlertSeverity.Warning,
            TerminalId = terminalId,
            Message = $"{terminalName}: granted time ran out. Session auto-ended ({totalDue:F2} due); terminal locked."
        };
        db.AlertEvents.Add(alert);
        await db.SaveChangesAsync();

        await _dashboards.Clients.Group("dashboard").AlertRaised(
            alert.Severity.ToString(), alert.Kind, alert.Message, terminalId, alert.CreatedAt);
    }
}

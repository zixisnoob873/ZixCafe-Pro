using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Server.App.Services;

public class AlertsCenterService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;

    public AlertsCenterService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        IHubContext<DashboardHub, IDashboardClient> dashboards)
    {
        _dbFactory = dbFactory;
        _dashboards = dashboards;
    }

    public async Task<AlertEvent?> RaiseAlertAsync(string severityStr, string kind, string message, Guid? terminalId, string? cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Check if kind is muted
        var now = DateTime.UtcNow;
        var muted = await db.AlertMutes.FirstOrDefaultAsync(m => m.Kind == kind);
        if (muted is not null && muted.MutedUntilUtc > now)
        {
            return null;
        }

        if (!Enum.TryParse<AlertSeverity>(severityStr, true, out var severity))
        {
            severity = AlertSeverity.Info;
        }

        var alert = new AlertEvent
        {
            Severity = severity,
            Kind = kind,
            Message = message,
            TerminalId = terminalId,
            CreatedAt = now
        };

        db.AlertEvents.Add(alert);
        await db.SaveChangesAsync();

        // Broadcast to dashboards
        await _dashboards.Clients.All.AlertRaised(severity.ToString().ToLowerInvariant(), kind, message, terminalId, now);

        return alert;
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var alerts = await db.AlertEvents
            .Include(a => a.Terminal)
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync();

        return alerts.Select(a => new AlertDto(
            a.Id,
            a.Severity.ToString(),
            a.Kind,
            a.Message,
            a.TerminalId,
            a.Terminal?.Name,
            a.CreatedAt,
            a.AcknowledgedAt != null,
            a.AcknowledgedBy,
            a.AcknowledgedAt
        )).ToList();
    }

    public async Task<ResultResponse> AcknowledgeAlertAsync(Guid alertId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var alert = await db.AlertEvents.FirstOrDefaultAsync(a => a.Id == alertId);
        if (alert is null)
        {
            return new ResultResponse(false, "Alert not found.");
        }

        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = cashierName;
        await db.SaveChangesAsync();

        var currentAlerts = await GetAlertsAsync();
        await _dashboards.Clients.All.AlertsUpdated(currentAlerts);

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> MuteAlertKindAsync(string kind, int minutes)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return new ResultResponse(false, "Alert kind cannot be empty.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var mute = await db.AlertMutes.FirstOrDefaultAsync(m => m.Kind == kind);
        var until = DateTime.UtcNow.AddMinutes(Math.Max(1, minutes));

        if (mute is null)
        {
            mute = new AlertMute { Kind = kind, MutedUntilUtc = until };
            db.AlertMutes.Add(mute);
        }
        else
        {
            mute.MutedUntilUtc = until;
        }

        await db.SaveChangesAsync();
        return new ResultResponse(true, null);
    }
}

using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class MaintenanceAndReservationService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly SessionService _sessions;

    public MaintenanceAndReservationService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        SessionService sessions)
    {
        _dbFactory = dbFactory;
        _sessions = sessions;
    }

    public async Task<ResultResponse> SetTerminalMaintenanceAsync(SetTerminalMaintenanceRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == request.TerminalId);
        if (terminal is null)
        {
            return new ResultResponse(false, "Terminal not found.");
        }

        if (request.InMaintenance)
        {
            // Cannot put into maintenance if active session is running
            var hasActiveSession = await db.Sessions.AnyAsync(s => s.TerminalId == terminal.Id && s.Status == SessionStatus.Active);
            if (hasActiveSession)
            {
                return new ResultResponse(false, "Cannot put terminal into maintenance while a session is running.");
            }

            terminal.Status = TerminalStatus.Maintenance;
            terminal.MaintenanceReason = request.Reason ?? "Under maintenance";
        }
        else
        {
            terminal.Status = TerminalStatus.Available;
            terminal.MaintenanceReason = null;
        }

        await AppendAuditAsync(db, "terminal.maintenance", terminal.Id.ToString(), $"inMaintenance={request.InMaintenance}, reason={request.Reason}", request.CashierName);
        await db.SaveChangesAsync();

        await _sessions.BroadcastStateAsync(terminal.Id);
        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> ReserveTerminalAsync(ReserveTerminalRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.GuestName))
        {
            return new ResultResponse(false, "Guest name is required for reservation.");
        }
        if (request.ReservedUntilUtc <= DateTime.UtcNow)
        {
            return new ResultResponse(false, "Reservation time must be in the future.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == request.TerminalId);
        if (terminal is null)
        {
            return new ResultResponse(false, "Terminal not found.");
        }

        if (terminal.Status == TerminalStatus.Maintenance)
        {
            return new ResultResponse(false, "Cannot reserve a terminal that is under maintenance.");
        }

        terminal.Status = TerminalStatus.Reserved;
        terminal.ReservedFor = request.GuestName.Trim();
        terminal.ReservedUntilUtc = request.ReservedUntilUtc;

        await AppendAuditAsync(db, "terminal.reserve", terminal.Id.ToString(), $"guest={request.GuestName}, until={request.ReservedUntilUtc:u}", request.CashierName);
        await db.SaveChangesAsync();

        await _sessions.BroadcastStateAsync(terminal.Id);
        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> ReleaseReservationAsync(Guid terminalId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == terminalId);
        if (terminal is null)
        {
            return new ResultResponse(false, "Terminal not found.");
        }

        terminal.Status = TerminalStatus.Available;
        terminal.ReservedFor = null;
        terminal.ReservedUntilUtc = null;

        await AppendAuditAsync(db, "terminal.release_reservation", terminal.Id.ToString(), "reservation released", cashierName);
        await db.SaveChangesAsync();

        await _sessions.BroadcastStateAsync(terminal.Id);
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

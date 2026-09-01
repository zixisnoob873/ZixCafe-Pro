using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZixCafe.Server.App.Services;

/// <summary>
/// Front-desk operations that live outside a single session:
/// shifts with drawer reconciliation, the walk-in waitlist, and item loans.
/// </summary>
public class DeskService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly SessionService _sessions;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboards;

    public DeskService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        SessionService sessions,
        IHubContext<TerminalHub, ITerminalClient> terminals,
        IHubContext<DashboardHub, IDashboardClient> dashboards)
    {
        _dbFactory = dbFactory;
        _sessions = sessions;
        _terminals = terminals;
        _dashboards = dashboards;
    }

    // ---------- shifts ----------

    public async Task<ShiftResponse> OpenShiftAsync(string cashierName, decimal openingFloat)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var open = await db.Shifts.FirstOrDefaultAsync(s => s.EndedAt == null);
        if (open is not null)
        {
            return new ShiftResponse(false, "A shift is already open.", null);
        }
        if (openingFloat < 0)
        {
            return new ShiftResponse(false, "Opening float can't be negative.", null);
        }

        var cashier = await db.Cashiers.FirstOrDefaultAsync(c => c.Name == cashierName && c.IsActive);
        if (cashier is null)
        {
            return new ShiftResponse(false, "Unknown cashier.", null);
        }

        var shift = new Shift
        {
            CashierId = cashier.Id,
            OpeningFloat = openingFloat
        };
        db.Shifts.Add(shift);

        await AppendAuditAsync(db, "shift.open", shift.Id.ToString(),
            $"float={openingFloat:F2}", cashierName);
        await db.SaveChangesAsync();

        return new ShiftResponse(true, null, ToDto(shift, cashier.Name));
    }

    public async Task<ShiftDto?> GetCurrentShiftAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var shift = await db.Shifts
            .Include(s => s.Cashier)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(s => s.EndedAt == null);
        return shift is null ? null : ToDto(shift, shift.Cashier.Name);
    }

    public async Task<ShiftResponse> CloseShiftAsync(string cashierName, decimal countedDrawer, string? note)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var shift = await db.Shifts
            .Include(s => s.Cashier)
            .FirstOrDefaultAsync(s => s.EndedAt == null);
        if (shift is null)
        {
            return new ShiftResponse(false, "No shift is open.", null);
        }

        // Honest approximation until split payments exist: every charge closed
        // during the shift is assumed to have been collected as cash at the desk.
        // SQLite can't SUM decimal in SQL, so total the (small) list client-side.
        var sessionTotals = await db.Sessions
            .Where(s => s.Status == SessionStatus.Completed
                && s.EndedAt != null
                && s.EndedAt >= shift.StartedAt)
            .Select(s => s.Amount)
            .ToListAsync();
        shift.ExpectedDrawer = shift.OpeningFloat + sessionTotals.Sum();
        shift.CountedDrawer = countedDrawer;
        shift.ClosingNote = note;
        shift.EndedAt = DateTime.UtcNow;

        await AppendAuditAsync(db, "shift.close", shift.Id.ToString(),
            $"expected={shift.ExpectedDrawer:F2} counted={countedDrawer:F2} variance={(shift.Variance ?? 0):F2}",
            cashierName);
        await db.SaveChangesAsync();

        return new ShiftResponse(true, null, ToDto(shift, shift.Cashier.Name));
    }

    private static ShiftDto ToDto(Shift shift, string cashierName) => new(
        shift.Id,
        cashierName,
        shift.OpeningFloat,
        shift.ExpectedDrawer,
        shift.CountedDrawer,
        shift.Variance,
        shift.StartedAt,
        shift.EndedAt,
        shift.EndedAt == null);

    // ---------- waitlist ----------

    public async Task<IReadOnlyList<WaitlistEntryDto>> GetWaitlistAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.WaitQueue
            .Where(w => w.Status == QueueStatus.Waiting)
            .OrderBy(w => w.EnqueuedAt)
            .Select(w => ToDto(w))
            .ToListAsync();
    }

    public async Task<WaitlistResponse> AddToWaitlistAsync(string guestName, int partySize, string? contact)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var name = (guestName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return new WaitlistResponse(false, "Guest name is required.", null);
        }
        if (partySize < 1)
        {
            partySize = 1;
        }

        var entry = new WaitQueueEntry
        {
            GuestName = name,
            PartySize = partySize,
            Contact = string.IsNullOrWhiteSpace(contact) ? null : contact.Trim()
        };
        db.WaitQueue.Add(entry);
        await db.SaveChangesAsync();

        await PushWaitlistAsync();
        return new WaitlistResponse(true, null, ToDto(entry));
    }

    public async Task<StartSessionResponse> SeatWaitlistGuestAsync(Guid entryId, Guid terminalId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = await db.WaitQueue.FirstOrDefaultAsync(w => w.Id == entryId);
        if (entry is null || entry.Status != QueueStatus.Waiting)
        {
            return new StartSessionResponse(false, null, "That guest is no longer waiting.", null, null);
        }

        var start = await _sessions.StartAsync(new StartSessionRequest(
            terminalId, "postpaid", null, null, null, cashierName));
        if (!start.Ok)
        {
            return start;
        }

        entry.Status = QueueStatus.Served;
        entry.ServedBy = cashierName;
        entry.ServedTerminalId = terminalId;
        entry.ServedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await PushWaitlistAsync();
        return start;
    }

    public async Task<WaitlistResponse> SkipWaitlistEntryAsync(Guid entryId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = await db.WaitQueue.FirstOrDefaultAsync(w => w.Id == entryId);
        if (entry is null || entry.Status != QueueStatus.Waiting)
        {
            return new WaitlistResponse(false, "That guest is no longer waiting.", null);
        }

        entry.Status = QueueStatus.Skipped;
        entry.ServedBy = cashierName;
        entry.ServedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await PushWaitlistAsync();
        return new WaitlistResponse(true, null, ToDto(entry));
    }

    private async Task PushWaitlistAsync()
    {
        var waiting = await GetWaitlistAsync();
        await _dashboards.Clients.Group("dashboard").WaitlistChanged(waiting);
    }

    private static WaitlistEntryDto ToDto(WaitQueueEntry w) => new(
        w.Id,
        w.GuestName,
        w.PartySize,
        w.Status.ToString(),
        w.Contact,
        w.EnqueuedAt,
        w.ServedTerminalId,
        w.ServedAt);

    // ---------- item loans ----------

    public async Task<IReadOnlyList<LoanDto>> GetLoansAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var open = await db.ItemLoans
            .Where(l => l.Status == LoanStatus.Held)
            .OrderBy(l => l.CreatedAt)
            .Select(l => ToDto(l))
            .ToListAsync();
        return open;
    }

    public async Task<LoanResponse> LoanItemAsync(string itemName, decimal deposit, string heldBy, Guid? sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var item = (itemName ?? string.Empty).Trim();
        if (item.Length == 0)
        {
            return new LoanResponse(false, "Item name is required.", null);
        }
        if (deposit < 0)
        {
            return new LoanResponse(false, "Deposit can't be negative.", null);
        }

        var loan = new ItemLoan
        {
            ItemName = item,
            DepositAmount = deposit,
            HeldBy = string.IsNullOrWhiteSpace(heldBy) ? null : heldBy.Trim(),
            SessionId = sessionId
        };
        db.ItemLoans.Add(loan);
        await db.SaveChangesAsync();

        return new LoanResponse(true, null, ToDto(loan));
    }

    public async Task<LoanResponse> ReturnLoanAsync(Guid loanId, string returnedTo, string cashierName, bool forfeited)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var loan = await db.ItemLoans.FirstOrDefaultAsync(l => l.Id == loanId && l.Status == LoanStatus.Held);
        if (loan is null)
        {
            return new LoanResponse(false, "That loan is not open.", null);
        }

        loan.Status = forfeited ? LoanStatus.Forfeited : LoanStatus.Returned;
        loan.ReturnedTo = string.IsNullOrWhiteSpace(returnedTo) ? null : returnedTo.Trim();
        loan.ReturnedAt = DateTime.UtcNow;

        await AppendAuditAsync(db, forfeited ? "loan.forfeit" : "loan.return", loan.Id.ToString(),
            $"{loan.ItemName} deposit={loan.DepositAmount:F2}", cashierName);
        await db.SaveChangesAsync();

        return new LoanResponse(true, null, ToDto(loan));
    }

    private static LoanDto ToDto(ItemLoan l) => new(
        l.Id,
        l.SessionId,
        l.ItemName,
        l.DepositAmount,
        l.Status.ToString(),
        l.HeldBy,
        l.ReturnedTo,
        l.CreatedAt,
        l.ReturnedAt);

    // ---------- lock all ----------

    public async Task<ResultResponse> LockAllTerminalsAsync(string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Lock idle (Available/Locked) terminals only — a lock push mid-session
        // would fight the running countdown, so in-use machines keep playing.
        var idleIds = await db.Terminals
            .Where(t => t.Status == TerminalStatus.Available || t.Status == TerminalStatus.Locked)
            .Select(t => t.Id)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var id in idleIds)
        {
            await _terminals.Clients.Group(TerminalGroups.Terminal(id)).ShowLockScreen(now);
            await _sessions.BroadcastStateAsync(id);
        }

        await AppendAuditAsync(db, "terminal.lock_all", "all",
            $"count={idleIds.Count}", cashierName);
        await db.SaveChangesAsync();
        return new ResultResponse(true, null);
    }

    private static async Task AppendAuditAsync(
        ZixCafeDbContext db, string action, string targetId, string detail, string cashierName)
    {
        var lastAudit = await db.AuditEntries.OrderBy(a => a.CreatedAt).LastOrDefaultAsync();
        db.AuditEntries.Add(DbInitializer.NewAudit(
            action, "Desk", targetId, detail, cashierName,
            prevHash: lastAudit?.Hash ?? string.Empty));
    }
}

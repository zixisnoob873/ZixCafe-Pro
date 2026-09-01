using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZixCafe.Server.App.Services;
public class SessionService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly TerminalRegistry _registry;
    private readonly IHubContext<TerminalHub, ITerminalClient> _terminals;
    private static readonly TimeZoneInfo VenueTimeZone = TimeZoneInfo.Local;

    public SessionService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        TerminalRegistry registry,
        IHubContext<TerminalHub, ITerminalClient> terminals)
    {
        _dbFactory = dbFactory;
        _registry = registry;
        _terminals = terminals;
    }

    public async Task<StartSessionResponse> StartAsync(StartSessionRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == request.TerminalId);
        if (terminal is null)
        {
            return Fail("Terminal not found.");
        }

        var active = await db.Sessions
            .FirstOrDefaultAsync(s => s.TerminalId == request.TerminalId && s.Status == SessionStatus.Active);
        if (active is not null)
        {
            return Fail("Terminal already has an active session.");
        }

        DateTime? plannedEnd = null;
        Guid? ticketId = null;
        decimal? depositDue = null;
        int? grantedMinutes = null;
        var mode = ParseMode(request.Mode);

        if (mode == SessionMode.Ticket)
        {
            var code = NormalizeTicketCode(request.TicketCode);
            if (!TicketCodeGenerator.IsValidFormat(code))
            {
                return Fail("That code doesn't look like a valid ticket. Check for transcription errors.");
            }
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Code == code && !t.IsUsed);
            if (ticket is null)
            {
                return Fail("Ticket not found or already redeemed.");
            }
            ticket.IsUsed = true;
            ticket.UsedAt = DateTime.UtcNow;
            ticketId = ticket.Id;
            if (ticket.Type == TicketType.Duration)
            {
                plannedEnd = DateTime.UtcNow.AddMinutes(ticket.DurationMinutes);
                grantedMinutes = ticket.DurationMinutes;
            }
            else
            {
                depositDue = ticket.CreditAmount;
            }
        }

        if (mode == SessionMode.Prepaid)
        {
            var minutes = request.PrepaidMinutes ?? 60;
            plannedEnd = DateTime.UtcNow.AddMinutes(minutes);
            grantedMinutes = minutes;
        }

        if (mode == SessionMode.Member)
        {
            var member = await db.Members.FirstOrDefaultAsync(m => m.Id == request.MemberId);
            if (member is null)
            {
                return Fail("Member not found.");
            }
            if (member.TimeBalanceMinutes <= 0 && member.MoneyBalance <= 0)
            {
                return Fail("Member has no balance.");
            }
            if (member.TimeBalanceMinutes > 0)
            {
                plannedEnd = DateTime.UtcNow.AddMinutes(member.TimeBalanceMinutes);
                grantedMinutes = member.TimeBalanceMinutes;
            }
        }

        var tariff = await db.Tariffs
            .Include(t => t.Rules)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.Priority)
            .FirstOrDefaultAsync();

        var session = new Session
        {
            TerminalId = request.TerminalId,
            MemberId = request.MemberId,
            TicketId = ticketId,
            TariffId = tariff?.Id,
            Mode = mode,
            Status = SessionStatus.Active,
            StartedAt = DateTime.UtcNow,
            PlannedEndAt = plannedEnd,
            OpenedBy = request.CashierName
        };
        db.Sessions.Add(session);

        if (ticketId is { } usedTicket)
        {
            (await db.Tickets.FirstAsync(t => t.Id == usedTicket)).UsedBySessionId = session.Id;
        }

        terminal.Status = TerminalStatus.InUse;
        terminal.IsLocked = false;

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        string? memberName = null;
        if (request.MemberId is { } mid)
        {
            memberName = (await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid))?.Name;
        }

        int? minutesGranted = plannedEnd is { } end ? (int)((end - DateTime.UtcNow).TotalMinutes) : null;
        await _terminals.Clients.Group(TerminalGroups.Terminal(request.TerminalId)).SessionStarted(
            session.Id, request.Mode, minutesGranted, plannedEnd, memberName);
        await _terminals.Clients.Group(TerminalGroups.Terminal(request.TerminalId)).TimeSync(
            DateTime.UtcNow, plannedEnd, 0m);

        await BroadcastStateAsync(request.TerminalId);

        return new StartSessionResponse(
            true, session.Id, null, grantedMinutes, depositDue);
    }

    public async Task<EndSessionResponse> EndAsync(EndSessionRequest request, string reason = "closed_by_cashier")
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var session = await db.Sessions
            .Include(s => s.Tariff)
            .ThenInclude(t => t!.Rules)
            .Include(s => s.Lines)
            .Include(s => s.Terminal)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId);

        if (session is null)
        {
            return FailEnd("Session not found.");
        }
        if (session.Status != SessionStatus.Active && session.Status != SessionStatus.Paused)
        {
            return FailEnd("Session already closed.");
        }

        session.EndedAt = DateTime.UtcNow;
        session.Status = SessionStatus.Completed;
        session.ClosedBy = request.CashierName;

        decimal timeCharge = 0;
        if (session.Tariff is not null)
        {
            timeCharge = TariffEngine.ComputeTimeCharge(
                session.Tariff,
                session.StartedAt,
                session.EndedAt!.Value,
                VenueTimeZone,
                session.PausedMinutes,
                out var billed);
            session.Amount = timeCharge;
        }

        var extras = session.Lines.Sum(l => l.Amount);
        var total = timeCharge + extras - session.CreditApplied;

        if (session.Mode == SessionMode.Member && session.MemberId is { } memberId)
        {
            var member = await db.Members.FirstAsync(m => m.Id == memberId);
            var billableMinutes = (int)Math.Ceiling(
                (session.EndedAt.Value - session.StartedAt - TimeSpan.FromMinutes(session.PausedMinutes)).TotalMinutes);
            if (billableMinutes < 0)
            {
                billableMinutes = 0;
            }
            if (member.TimeBalanceMinutes > 0)
            {
                var delta = -Math.Min(member.TimeBalanceMinutes, billableMinutes);
                member.TimeBalanceMinutes += delta;
                db.MemberTransactions.Add(new MemberTransaction
                {
                    MemberId = memberId,
                    Kind = "session.time",
                    Amount = 0,
                    BalanceAfter = member.MoneyBalance,
                    TimeMinutesDelta = delta,
                    TimeBalanceAfter = member.TimeBalanceMinutes,
                    CashierName = request.CashierName,
                    Note = $"session {session.Id.ToString()[..8]}"
                });
            }
            else
            {
                member.MoneyBalance = Math.Max(0, member.MoneyBalance - total);
                db.MemberTransactions.Add(new MemberTransaction
                {
                    MemberId = memberId,
                    Kind = "session.money",
                    Amount = -total,
                    BalanceAfter = member.MoneyBalance,
                    TimeMinutesDelta = 0,
                    TimeBalanceAfter = member.TimeBalanceMinutes,
                    CashierName = request.CashierName,
                    Note = $"session {session.Id.ToString()[..8]}"
                });
            }
        }

        session.Terminal.Status = TerminalStatus.Available;
        session.Terminal.IsLocked = true;

        await AppendAuditAsync(db, reason == "time_up" ? "session.auto_end" : "session.end",
            session.Id.ToString(), $"total={total:F2} time={timeCharge:F2}", request.CashierName);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await _terminals.Clients.Group(TerminalGroups.Terminal(session.TerminalId)).SessionEnded(
            reason, DateTime.UtcNow);
        await _terminals.Clients.Group(TerminalGroups.Terminal(session.TerminalId)).ShowLockScreen(DateTime.UtcNow);

        await BroadcastStateAsync(session.TerminalId);

        return new EndSessionResponse(
            true, null, timeCharge, extras, total,
            session.Lines.Select(l => new LineDto(l.Kind.ToString(), l.Description, l.Quantity, l.UnitAmount, l.Amount)).ToList());
    }

    public async Task<Session?> GetActiveAsync(Guid terminalId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.TerminalId == terminalId && s.Status == SessionStatus.Active);
    }

    public async Task BroadcastStateAsync(Guid terminalId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var terminal = await db.Terminals
            .Include(t => t.Zone)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == terminalId);
        if (terminal is null)
        {
            return;
        }

        var active = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Tariff).ThenInclude(t => t!.Rules)
            .FirstOrDefaultAsync(s => s.TerminalId == terminalId
                && (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused));

        var now = DateTime.UtcNow;
        var chargeClock = active is { Status: SessionStatus.Paused } && active.PausedAtUtc is { } pausedAt
            ? pausedAt
            : now;
        var elapsed = active is null
            ? 0
            : (int)((chargeClock - active.StartedAt - TimeSpan.FromMinutes(active.PausedMinutes)).TotalMinutes);
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var amount = 0m;
        int? remaining = null;
        if (active is not null && active.Tariff is not null)
        {
            amount = TariffEngine.ComputeTimeCharge(
                active.Tariff, active.StartedAt, chargeClock, VenueTimeZone,
                active.PausedMinutes, out _);
            amount += active.Lines.Sum(l => l.Amount);
            if (active.PlannedEndAt is { } plannedEnd)
            {
                remaining = (int)Math.Max(0, (plannedEnd - chargeClock).TotalMinutes);
            }
        }

        _registry.RaiseState(new TerminalStateDto(
            terminal.Id,
            terminal.Name,
            terminal.Zone.Name,
            (TerminalStatusDto)terminal.Status,
            terminal.IsLocked,
            terminal.AgentVersion,
            terminal.LastSeenAt,
            active?.Id,
            amount,
            elapsed,
            remaining,
            active?.PlannedEndAt,
            active is { Status: SessionStatus.Paused },
            terminal.MaintenanceReason,
            terminal.ReservedFor,
            terminal.CpuTemp,
            terminal.GpuTemp,
            terminal.RamPercent));
    }

    public async Task<ResultResponse> PauseAsync(Guid terminalId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.TerminalId == terminalId && s.Status == SessionStatus.Active);
        if (session is null)
        {
            return new ResultResponse(false, "No active session to pause.");
        }

        session.Status = SessionStatus.Paused;
        session.PausedAtUtc = DateTime.UtcNow;
        await AppendAuditAsync(db, "session.pause", session.Id.ToString(),
            $"terminal={terminalId}", cashierName);
        await db.SaveChangesAsync();

        await _terminals.Clients.Group(TerminalGroups.Terminal(terminalId)).SessionPaused(DateTime.UtcNow);
        await BroadcastStateAsync(terminalId);
        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> ResumeAsync(Guid terminalId, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.TerminalId == terminalId && s.Status == SessionStatus.Paused);
        if (session is null)
        {
            return new ResultResponse(false, "No paused session on this terminal.");
        }

        var pausedSpan = session.PausedAtUtc is { } pausedAt
            ? DateTime.UtcNow - pausedAt
            : TimeSpan.Zero;
        session.PausedMinutes += (int)Math.Ceiling(pausedSpan.TotalMinutes);
        if (session.PlannedEndAt is { } plannedEnd)
        {
            session.PlannedEndAt = plannedEnd + pausedSpan;
        }
        session.Status = SessionStatus.Active;
        session.PausedAtUtc = null;
        await AppendAuditAsync(db, "session.resume", session.Id.ToString(),
            $"pausedMin={session.PausedMinutes}", cashierName);
        await db.SaveChangesAsync();

        await _terminals.Clients.Group(TerminalGroups.Terminal(terminalId)).SessionResumed(
            DateTime.UtcNow, session.PlannedEndAt);
        await BroadcastStateAsync(terminalId);
        return new ResultResponse(true, null);
    }

    public async Task<FindMemberResponse> FindMemberAsync(string query)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var term = (query ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            return new FindMemberResponse(false, "Enter a member code or name.", null);
        }
        var member = await db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == term
                || m.Name.ToLower().StartsWith(term.ToLower()));
        if (member is null || !member.IsActive)
        {
            return new FindMemberResponse(false, "No active member matches that code or name.", null);
        }
        return new FindMemberResponse(true, null, new MemberDto(
            member.Id, member.Code, member.Name, member.TimeBalanceMinutes, member.MoneyBalance));
    }

    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProductDto(p.Id, p.Sku, p.Name, p.Price, p.StockQty))
            .ToListAsync();
    }

    public async Task<AddLineResponse> AddProductLineAsync(
        Guid sessionId, Guid productId, decimal quantity, string cashierName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var session = await db.Sessions
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null || session.Status is not (SessionStatus.Active or SessionStatus.Paused))
        {
            return new AddLineResponse(false, "Session is not running.", 0);
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId && p.IsActive);
        if (product is null)
        {
            return new AddLineResponse(false, "Product not found.", 0);
        }
        if (quantity <= 0)
        {
            quantity = 1;
        }
        if (product.StockQty < quantity)
        {
            return new AddLineResponse(false, $"Only {product.StockQty} in stock.", 0);
        }

        product.StockQty -= (int)quantity;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Reason = StockReason.Sale,
            Delta = -(int)quantity,
            StockAfter = product.StockQty,
            Reference = session.Id.ToString()[..8],
            CashierName = cashierName
        });
        db.SessionLines.Add(new SessionLine
        {
            SessionId = session.Id,
            Kind = LineKind.Product,
            Description = product.Name,
            Quantity = quantity,
            UnitAmount = product.Price,
            Amount = decimal.Round(product.Price * quantity, 2)
        });

        await AppendAuditAsync(db, "sale.extras", session.Id.ToString(),
            $"{product.Sku} x{quantity}={decimal.Round(product.Price * quantity, 2):F2}", cashierName);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await BroadcastStateAsync(session.TerminalId);
        return new AddLineResponse(true, null, session.Lines.Sum(l => l.Amount));
    }

    public LoginResponse Login(LoginRequest request)
    {
        using var db = _dbFactory.CreateDbContext();
        var cashier = db.Cashiers.FirstOrDefault(c => c.Name == request.Name && c.IsActive);
        if (cashier is null || !SecretHasher.Verify(request.Pin, cashier.PinHash))
        {
            return new LoginResponse(false, "Unknown cashier or wrong PIN.", string.Empty);
        }
        return new LoginResponse(true, null, cashier.Role.ToString());
    }

    private static async Task AppendAuditAsync(
        ZixCafeDbContext db, string action, string targetId, string detail, string cashierName)
    {
        var lastAudit = await db.AuditEntries.OrderBy(a => a.CreatedAt).LastOrDefaultAsync();
        db.AuditEntries.Add(DbInitializer.NewAudit(
            action, "Session", targetId, detail, cashierName,
            prevHash: lastAudit?.Hash ?? string.Empty));
    }

    private static SessionMode ParseMode(string mode) => mode switch
    {
        "prepaid" => SessionMode.Prepaid,
        "member" => SessionMode.Member,
        "ticket" => SessionMode.Ticket,
        _ => SessionMode.Postpaid
    };

    private static string NormalizeTicketCode(string? input) => string
        .Concat((input ?? string.Empty).Where(char.IsLetterOrDigit))
        .ToUpperInvariant();

    private static StartSessionResponse Fail(string error) => new(false, null, error, null, null);

    private static EndSessionResponse FailEnd(string error) => new(false, error, 0, 0, 0, Array.Empty<LineDto>());
}

using Microsoft.EntityFrameworkCore;
using System.Text;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class ReportsAndAuditService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;

    public ReportsAndAuditService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ShiftReportDto?> GetShiftReportAsync(Guid shiftId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var shift = await db.Shifts
            .Include(s => s.Cashier)
            .FirstOrDefaultAsync(s => s.Id == shiftId);

        if (shift is null)
        {
            return null;
        }

        var shiftEnd = shift.EndedAt ?? DateTime.UtcNow;

        // Collect completed sessions during this shift
        var sessions = await db.Sessions
            .Include(s => s.Lines)
            .Where(s => s.Status == SessionStatus.Completed && s.EndedAt != null && s.EndedAt >= shift.StartedAt && s.EndedAt <= shiftEnd)
            .ToListAsync();

        var timeRevenue = sessions.Sum(s => s.Amount);
        var extrasRevenue = sessions.SelectMany(s => s.Lines).Sum(l => l.Amount);

        // Collect retail sales during this shift
        var sales = await db.Sales
            .Include(s => s.Lines)
            .Where(s => s.CreatedAt >= shift.StartedAt && s.CreatedAt <= shiftEnd)
            .ToListAsync();

        var productRevenue = sales.SelectMany(s => s.Lines).Where(l => l.Kind == LineKind.Product).Sum(l => l.Amount);
        var printUsbRevenue = sales.SelectMany(s => s.Lines).Where(l => l.Kind == LineKind.Print || l.Kind == LineKind.Usb).Sum(l => l.Amount);
        var discountsTotal = sales.Sum(s => s.Discount);
        var adjustmentsTotal = sales.SelectMany(s => s.Lines).Where(l => l.Kind == LineKind.Adjustment).Sum(l => l.Amount);

        var totalCashSales = sales.Sum(s => s.PaidCash - s.ChangeDue);

        var expectedDrawer = shift.OpeningFloat + totalCashSales;

        return new ShiftReportDto(
            shift.Id,
            shift.Cashier.Name,
            shift.StartedAt,
            shift.EndedAt,
            shift.OpeningFloat,
            timeRevenue,
            productRevenue,
            printUsbRevenue,
            discountsTotal,
            adjustmentsTotal,
            expectedDrawer,
            shift.CountedDrawer,
            shift.Variance,
            sessions.Count,
            sales.Count
        );
    }

    public async Task<IReadOnlyList<DailyRevenueDto>> GetDailyRevenueReportAsync(DateTime fromDateUtc, DateTime toDateUtc)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var sessions = await db.Sessions
            .Where(s => s.Status == SessionStatus.Completed && s.EndedAt != null && s.EndedAt >= fromDateUtc && s.EndedAt <= toDateUtc)
            .ToListAsync();

        var sales = await db.Sales
            .Include(s => s.Lines)
            .Where(s => s.CreatedAt >= fromDateUtc && s.CreatedAt <= toDateUtc)
            .ToListAsync();

        var days = (toDateUtc.Date - fromDateUtc.Date).Days + 1;
        var result = new List<DailyRevenueDto>();

        for (var i = 0; i < days; i++)
        {
            var dayStart = fromDateUtc.Date.AddDays(i);
            var dayEnd = dayStart.AddDays(1);

            var daySessions = sessions.Where(s => s.EndedAt >= dayStart && s.EndedAt < dayEnd).ToList();
            var daySales = sales.Where(s => s.CreatedAt >= dayStart && s.CreatedAt < dayEnd).ToList();

            var timeRev = daySessions.Sum(s => s.Amount);
            var prodRev = daySales.SelectMany(s => s.Lines).Where(l => l.Kind == LineKind.Product).Sum(l => l.Amount);
            var otherRev = daySales.SelectMany(s => s.Lines).Where(l => l.Kind != LineKind.Product).Sum(l => l.Amount);
            var totalRev = timeRev + prodRev + otherRev;

            result.Add(new DailyRevenueDto(
                dayStart,
                timeRev,
                prodRev,
                otherRev,
                totalRev,
                daySessions.Count
            ));
        }

        return result;
    }

    public async Task<IReadOnlyList<SessionHistoryDto>> GetSessionHistoryAsync(DateTime fromDateUtc, DateTime toDateUtc, Guid? terminalId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Sessions
            .Include(s => s.Terminal)
            .Include(s => s.Member)
            .Include(s => s.Ticket)
            .Where(s => s.StartedAt >= fromDateUtc && s.StartedAt <= toDateUtc)
            .AsQueryable();

        if (terminalId.HasValue && terminalId.Value != Guid.Empty)
        {
            query = query.Where(s => s.TerminalId == terminalId.Value);
        }

        var list = await query
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync();

        return list.Select(s =>
        {
            var duration = s.EndedAt.HasValue
                ? (int)(s.EndedAt.Value - s.StartedAt).TotalMinutes
                : (int)(DateTime.UtcNow - s.StartedAt).TotalMinutes;

            return new SessionHistoryDto(
                s.Id,
                s.Terminal.Name,
                s.Mode.ToString(),
                s.Member?.Name,
                s.Ticket?.Code,
                s.Amount,
                s.StartedAt,
                s.EndedAt,
                Math.Max(0, duration),
                s.ClosedBy
            );
        }).ToList();
    }

    public async Task<IReadOnlyList<AuditEntryDto>> GetAuditEntriesAsync(int limit)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var list = await db.AuditEntries
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return list.Select(a => new AuditEntryDto(
            a.Id,
            a.Action,
            a.TargetType,
            a.TargetId,
            a.DetailJson,
            a.CashierName,
            a.PrevHash,
            a.Hash,
            a.CreatedAt
        )).ToList();
    }

    public async Task<AuditVerificationResult> VerifyAuditChainAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entries = await db.AuditEntries
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync();

        if (entries.Count == 0)
        {
            return new AuditVerificationResult(true, 0, null, null);
        }

        string expectedPrevHash = string.Empty;
        int checkedCount = 0;

        foreach (var entry in entries)
        {
            if (entry.PrevHash != expectedPrevHash)
            {
                return new AuditVerificationResult(
                    false,
                    checkedCount,
                    entry.Id.ToString(),
                    $"Hash chain broken at entry #{checkedCount + 1} ({entry.Action}). Expected PrevHash '{expectedPrevHash}' but found '{entry.PrevHash}'."
                );
            }

            var (_, computedHash) = AuditChain.Link(
                entry.PrevHash,
                entry.Action,
                entry.TargetType,
                entry.TargetId,
                entry.DetailJson,
                entry.CashierName,
                entry.CreatedAt
            );

            if (computedHash != entry.Hash)
            {
                return new AuditVerificationResult(
                    false,
                    checkedCount,
                    entry.Id.ToString(),
                    $"Cryptographic tamper detected at entry #{checkedCount + 1} ({entry.Action}). Computed hash '{computedHash}' does not match stored hash '{entry.Hash}'."
                );
            }

            expectedPrevHash = entry.Hash;
            checkedCount++;
        }

        return new AuditVerificationResult(true, checkedCount, null, null);
    }

    public string ExportRevenueToCsv(IReadOnlyList<DailyRevenueDto> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Time Revenue,Product Revenue,Other Revenue,Total Revenue,Sessions");
        foreach (var row in data)
        {
            sb.AppendLine($"{row.Date:yyyy-MM-dd},{row.TimeRevenue:F2},{row.ProductRevenue:F2},{row.OtherRevenue:F2},{row.TotalRevenue:F2},{row.TotalSessions}");
        }
        return sb.ToString();
    }

    public string ExportSessionHistoryToCsv(IReadOnlyList<SessionHistoryDto> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SessionId,Terminal,Mode,Member,Ticket,Total Amount,Started At,Ended At,Duration Minutes,Ended By");
        foreach (var row in data)
        {
            sb.AppendLine($"\"{row.Id}\",\"{row.TerminalName}\",\"{row.Mode}\",\"{row.MemberName}\",\"{row.TicketCode}\",{row.TotalAmount:F2},\"{row.StartedAt:yyyy-MM-dd HH:mm:ss}\",\"{row.EndedAt:yyyy-MM-dd HH:mm:ss}\",{row.DurationMinutes},\"{row.EndedBy}\"");
        }
        return sb.ToString();
    }
}

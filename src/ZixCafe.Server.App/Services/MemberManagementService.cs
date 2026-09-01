using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class MemberManagementService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;

    public MemberManagementService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<MemberDetailDto>> GetMembersAsync(string? search)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Members.Include(m => m.Tier).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(m => m.Code.ToLower().Contains(s)
                || m.Name.ToLower().Contains(s)
                || (m.Phone != null && m.Phone.ToLower().Contains(s))
                || (m.Email != null && m.Email.ToLower().Contains(s)));
        }

        var list = await query
            .OrderBy(m => m.Code)
            .Take(100)
            .ToListAsync();

        return list.Select(m => new MemberDetailDto(
            m.Id,
            m.Code,
            m.Name,
            m.Phone,
            m.Email,
            m.Notes,
            m.TierId,
            m.Tier?.Name,
            m.TimeBalanceMinutes,
            m.MoneyBalance,
            m.IsFrozen,
            m.IsActive,
            m.CreatedAt
        )).ToList();
    }

    public async Task<MemberDetailDto?> GetMemberDetailAsync(Guid memberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var m = await db.Members
            .Include(m => m.Tier)
            .FirstOrDefaultAsync(x => x.Id == memberId);

        if (m is null)
        {
            return null;
        }

        return new MemberDetailDto(
            m.Id,
            m.Code,
            m.Name,
            m.Phone,
            m.Email,
            m.Notes,
            m.TierId,
            m.Tier?.Name,
            m.TimeBalanceMinutes,
            m.MoneyBalance,
            m.IsFrozen,
            m.IsActive,
            m.CreatedAt
        );
    }

    public async Task<ResultResponse> SaveMemberAsync(SaveMemberRequest request, string requestingCashier)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ResultResponse(false, "Member name cannot be empty.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        Member? member = null;

        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            member = await db.Members.FirstOrDefaultAsync(m => m.Id == request.Id.Value);
            if (member is null)
            {
                return new ResultResponse(false, "Member not found.");
            }
        }
        else
        {
            // Generate next M-#### code
            var count = await db.Members.CountAsync();
            var code = $"M-{(count + 1):0000}";
            while (await db.Members.AnyAsync(m => m.Code == code))
            {
                count++;
                code = $"M-{(count + 1):0000}";
            }

            member = new Member
            {
                Code = code,
                CreatedAt = DateTime.UtcNow
            };
            db.Members.Add(member);
        }

        member.Name = request.Name.Trim();
        member.Phone = request.Phone?.Trim();
        member.Email = request.Email?.Trim();
        member.Notes = request.Notes?.Trim();
        member.TierId = request.TierId;

        await AppendAuditAsync(db, "member.save", member.Id.ToString(), $"code={member.Code}, name={member.Name}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> TopUpMemberAsync(MemberTopUpRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var member = await db.Members.Include(m => m.Tier).FirstOrDefaultAsync(m => m.Id == request.MemberId);
        if (member is null)
        {
            return new ResultResponse(false, "Member not found.");
        }
        if (member.IsFrozen)
        {
            return new ResultResponse(false, "Cannot top up a frozen member account.");
        }

        if (request.Kind == "Money")
        {
            if (request.Amount <= 0)
            {
                return new ResultResponse(false, "Top-up amount must be positive.");
            }

            member.MoneyBalance += request.Amount;

            // Auto tier upgrade check
            var higherTier = await db.MemberTiers
                .Where(t => t.MinTopUpAmount > 0 && request.Amount >= t.MinTopUpAmount)
                .OrderByDescending(t => t.Priority)
                .FirstOrDefaultAsync();
            if (higherTier is not null && (member.Tier == null || higherTier.Priority > member.Tier.Priority))
            {
                member.TierId = higherTier.Id;
            }

            db.MemberTransactions.Add(new MemberTransaction
            {
                MemberId = member.Id,
                Kind = "topup.money",
                Amount = request.Amount,
                BalanceAfter = member.MoneyBalance,
                TimeMinutesDelta = 0,
                TimeBalanceAfter = member.TimeBalanceMinutes,
                CashierName = request.CashierName,
                Note = $"Counter Top-Up ({request.PaymentMethod})",
                CreatedAt = DateTime.UtcNow
            });

            // Register retail sale
            db.Sales.Add(new Sale
            {
                CashierName = request.CashierName,
                CustomerName = member.Name,
                Subtotal = request.Amount,
                Total = request.Amount,
                PaidCash = request.PaymentMethod == "Cash" ? request.Amount : 0m,
                PaidCard = request.PaymentMethod == "Card" ? request.Amount : 0m,
                PaidQr = request.PaymentMethod == "QR" ? request.Amount : 0m,
                PaymentMethod = request.PaymentMethod,
                Note = $"Member top-up: {member.Code}",
                CreatedAt = DateTime.UtcNow,
                Lines =
                {
                    new SaleLine
                    {
                        Kind = LineKind.Product,
                        Description = $"Member Money Top-Up ({member.Code})",
                        Quantity = 1,
                        UnitAmount = request.Amount,
                        Amount = request.Amount
                    }
                }
            });
        }
        else if (request.Kind == "Time")
        {
            if (request.Minutes <= 0)
            {
                return new ResultResponse(false, "Time top-up minutes must be positive.");
            }

            member.TimeBalanceMinutes += request.Minutes;

            db.MemberTransactions.Add(new MemberTransaction
            {
                MemberId = member.Id,
                Kind = "topup.time",
                Amount = request.Amount,
                BalanceAfter = member.MoneyBalance,
                TimeMinutesDelta = request.Minutes,
                TimeBalanceAfter = member.TimeBalanceMinutes,
                CashierName = request.CashierName,
                Note = $"Time Top-Up {request.Minutes}m ({request.PaymentMethod})",
                CreatedAt = DateTime.UtcNow
            });

            if (request.Amount > 0)
            {
                db.Sales.Add(new Sale
                {
                    CashierName = request.CashierName,
                    CustomerName = member.Name,
                    Subtotal = request.Amount,
                    Total = request.Amount,
                    PaidCash = request.PaymentMethod == "Cash" ? request.Amount : 0m,
                    PaidCard = request.PaymentMethod == "Card" ? request.Amount : 0m,
                    PaidQr = request.PaymentMethod == "QR" ? request.Amount : 0m,
                    PaymentMethod = request.PaymentMethod,
                    Note = $"Member time top-up: {member.Code} ({request.Minutes}m)",
                    CreatedAt = DateTime.UtcNow,
                    Lines =
                    {
                        new SaleLine
                        {
                            Kind = LineKind.Time,
                            Description = $"Member Time Top-Up {request.Minutes}m ({member.Code})",
                            Quantity = 1,
                            UnitAmount = request.Amount,
                            Amount = request.Amount
                        }
                    }
                });
            }
        }

        await AppendAuditAsync(db, "member.topup", member.Id.ToString(), $"kind={request.Kind}, amount={request.Amount}, minutes={request.Minutes}", request.CashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<IReadOnlyList<MemberTransactionDto>> GetMemberTransactionsAsync(Guid memberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var txs = await db.MemberTransactions
            .Where(t => t.MemberId == memberId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync();

        return txs.Select(t => new MemberTransactionDto(
            t.Id,
            t.MemberId,
            t.Kind,
            t.Amount,
            t.TimeMinutesDelta,
            t.Note,
            t.CashierName,
            t.CreatedAt
        )).ToList();
    }

    public async Task<IReadOnlyList<MemberTierDto>> GetMemberTiersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tiers = await db.MemberTiers
            .OrderBy(t => t.Priority)
            .ToListAsync();

        return tiers.Select(t => new MemberTierDto(
            t.Id,
            t.Name,
            t.DiscountPercent,
            t.MinTopUpAmount,
            t.Priority
        )).ToList();
    }

    public async Task<ResultResponse> SetMemberFrozenAsync(Guid memberId, bool isFrozen, string requestingCashier)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == memberId);
        if (member is null)
        {
            return new ResultResponse(false, "Member not found.");
        }

        member.IsFrozen = isFrozen;
        await AppendAuditAsync(db, "member.freeze", member.Id.ToString(), $"frozen={isFrozen}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    private static async Task AppendAuditAsync(ZixCafeDbContext db, string action, string? targetId, string? detail, string cashier)
    {
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "Member", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "Member",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
    }
}

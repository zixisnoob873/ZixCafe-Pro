using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class TicketService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly AuthAndCashierService _auth;

    public TicketService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        AuthAndCashierService auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;
    }

    public async Task<IReadOnlyList<TicketDto>> GetTicketsAsync(bool unusedOnly)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Tickets.AsQueryable();
        if (unusedOnly)
        {
            query = query.Where(t => !t.IsUsed);
        }

        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(150)
            .ToListAsync();

        return tickets.Select(t => new TicketDto(
            t.Id,
            FormatCode(t.Code),
            t.Type.ToString(),
            t.DurationMinutes,
            t.CreditAmount,
            t.Price,
            t.IsUsed,
            t.UsedAt,
            t.IssuedBy,
            t.BatchRef,
            t.CreatedAt,
            t.ExpiresAt
        )).ToList();
    }

    public async Task<ResultResponse> SellTicketAsync(SellTicketRequest request)
    {
        if (request.Price < 0)
        {
            return new ResultResponse(false, "Price cannot be negative.");
        }

        if (!Enum.TryParse<TicketType>(request.Type, true, out var type))
        {
            type = TicketType.Duration;
        }

        if (type == TicketType.Duration && (!request.DurationMinutes.HasValue || request.DurationMinutes.Value <= 0))
        {
            return new ResultResponse(false, "Duration minutes must be positive.");
        }

        if (type == TicketType.Credit && (!request.CreditAmount.HasValue || request.CreditAmount.Value <= 0))
        {
            return new ResultResponse(false, "Credit amount must be positive.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var code = TicketCodeGenerator.NewCode(RandomNumberGenerator.Create()).Replace("-", string.Empty);
        var ticket = new TicketVoucher
        {
            Code = code,
            Type = type,
            DurationMinutes = request.DurationMinutes ?? 0,
            CreditAmount = request.CreditAmount ?? 0m,
            Price = request.Price,
            IssuedBy = request.CashierName,
            CreatedAt = DateTime.UtcNow
        };

        db.Tickets.Add(ticket);

        // Also record retail sale if price > 0
        if (request.Price > 0)
        {
            var sale = new Sale
            {
                CashierName = request.CashierName,
                Subtotal = request.Price,
                Discount = 0m,
                Total = request.Price,
                PaidCash = request.PaymentMethod == "Cash" ? request.Price : 0m,
                PaidCard = request.PaymentMethod == "Card" ? request.Price : 0m,
                PaidQr = request.PaymentMethod == "QR" ? request.Price : 0m,
                PaymentMethod = request.PaymentMethod,
                Note = $"Ticket sale ({ticket.Type})",
                CreatedAt = DateTime.UtcNow,
                Lines =
                {
                    new SaleLine
                    {
                        Kind = LineKind.Product,
                        Description = $"Ticket Voucher - {ticket.Type} ({FormatCode(code)})",
                        Quantity = 1,
                        UnitAmount = request.Price,
                        Amount = request.Price
                    }
                }
            };
            db.Sales.Add(sale);
        }

        await AppendAuditAsync(db, "ticket.sell", ticket.Id.ToString(), $"code={FormatCode(code)}, type={type}, price={request.Price}", request.CashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> BatchGenerateTicketsAsync(BatchGenerateTicketsRequest request)
    {
        if (request.Count <= 0 || request.Count > 100)
        {
            return new ResultResponse(false, "Batch count must be between 1 and 100.");
        }
        if (request.Price < 0)
        {
            return new ResultResponse(false, "Price cannot be negative.");
        }

        if (!Enum.TryParse<TicketType>(request.Type, true, out var type))
        {
            type = TicketType.Duration;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var rng = RandomNumberGenerator.Create();
        var tickets = new List<TicketVoucher>();

        for (var i = 0; i < request.Count; i++)
        {
            var code = TicketCodeGenerator.NewCode(rng).Replace("-", string.Empty);
            tickets.Add(new TicketVoucher
            {
                Code = code,
                Type = type,
                DurationMinutes = request.DurationMinutes ?? 0,
                CreditAmount = request.CreditAmount ?? 0m,
                Price = request.Price,
                BatchRef = string.IsNullOrWhiteSpace(request.BatchRef) ? $"BATCH-{DateTime.UtcNow:yyyyMMdd}" : request.BatchRef.Trim(),
                IssuedBy = request.CashierName,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.Tickets.AddRange(tickets);
        await AppendAuditAsync(db, "ticket.batch_generate", null, $"count={request.Count}, type={type}, batchRef={request.BatchRef}", request.CashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> VoidTicketAsync(Guid ticketId, string cashierName, string managerPin)
    {
        if (!await _auth.VerifyManagerPinAsync(managerPin))
        {
            return new ResultResponse(false, "Invalid Manager PIN. Manager authorization required to void tickets.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null)
        {
            return new ResultResponse(false, "Ticket not found.");
        }
        if (ticket.IsUsed)
        {
            return new ResultResponse(false, "Cannot void a ticket that has already been redeemed.");
        }

        db.Tickets.Remove(ticket);
        await AppendAuditAsync(db, "ticket.void", ticketId.ToString(), $"voided ticket {FormatCode(ticket.Code)} (Manager Override)", cashierName);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    private static string FormatCode(string compact)
    {
        if (compact.Length == 13)
        {
            return $"{compact[..4]}-{compact.Substring(4, 4)}-{compact.Substring(8, 4)}-{compact[12]}";
        }
        return compact;
    }

    private static async Task AppendAuditAsync(ZixCafeDbContext db, string action, string? targetId, string? detail, string cashier)
    {
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "Ticket", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "Ticket",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
    }
}

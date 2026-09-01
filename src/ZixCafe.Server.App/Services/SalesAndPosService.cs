using Microsoft.EntityFrameworkCore;
using System.Text;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class SalesAndPosService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly AlertsCenterService _alerts;
    private readonly VenueSettingsService _venueSettings;

    public SalesAndPosService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        AlertsCenterService alerts,
        VenueSettingsService venueSettings)
    {
        _dbFactory = dbFactory;
        _alerts = alerts;
        _venueSettings = venueSettings;
    }

    public async Task<ResultResponse> CreateSaleAsync(CreateSaleRequest request)
    {
        if (request.Lines.Count == 0)
        {
            return new ResultResponse(false, "Sale must have at least one line item.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Find active cashier
        var cashier = await db.Cashiers.FirstOrDefaultAsync(c => c.Name == request.CashierName && c.IsActive);

        decimal subtotal = 0m;
        var saleLines = new List<SaleLine>();

        foreach (var reqLine in request.Lines)
        {
            if (reqLine.Quantity <= 0)
            {
                return new ResultResponse(false, $"Invalid quantity for {reqLine.Description}.");
            }

            var lineAmount = Math.Max(0m, (reqLine.Quantity * reqLine.UnitAmount) - reqLine.DiscountAmount);
            subtotal += lineAmount;

            if (!Enum.TryParse<LineKind>(reqLine.Kind, true, out var kind))
            {
                kind = LineKind.Product;
            }

            var saleLine = new SaleLine
            {
                ProductId = reqLine.ProductId,
                Kind = kind,
                Description = reqLine.Description,
                Quantity = reqLine.Quantity,
                UnitAmount = reqLine.UnitAmount,
                DiscountAmount = reqLine.DiscountAmount,
                Amount = lineAmount
            };
            saleLines.Add(saleLine);

            // If product, decrement inventory and create stock movement
            if (reqLine.ProductId.HasValue && reqLine.ProductId.Value != Guid.Empty)
            {
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == reqLine.ProductId.Value);
                if (product is not null)
                {
                    var qtyInt = (int)Math.Ceiling(reqLine.Quantity);
                    if (product.StockQty < qtyInt)
                    {
                        return new ResultResponse(false, $"Insufficient stock for {product.Name}. In stock: {product.StockQty}");
                    }

                    product.StockQty -= qtyInt;

                    db.StockMovements.Add(new StockMovement
                    {
                        ProductId = product.Id,
                        Delta = -qtyInt,
                        StockAfter = product.StockQty,
                        Reason = StockReason.Sale,
                        Reference = "POS Sale",
                        CashierName = request.CashierName
                    });

                    if (product.StockQty <= product.LowStockThreshold)
                    {
                        await _alerts.RaiseAlertAsync("warn", "stock.low",
                            $"Low stock warning: {product.Name} (SKU: {product.Sku}) has {product.StockQty} remaining.",
                            null, request.CashierName);
                    }
                }
            }
        }

        var total = Math.Max(0m, subtotal - request.Discount);
        var totalTendered = request.PaidCash + request.PaidCard + request.PaidQr;

        if (totalTendered < total)
        {
            return new ResultResponse(false, $"Insufficient payment. Total due is {total:0.00}, but received {totalTendered:0.00}. Full payment required.");
        }

        var changeDue = totalTendered > total && request.PaidCash > 0
            ? Math.Min(totalTendered - total, request.PaidCash)
            : 0m;

        var sale = new Sale
        {
            CashierId = cashier?.Id,
            CashierName = request.CashierName,
            SessionId = request.SessionId,
            CustomerName = request.CustomerName,
            Subtotal = subtotal,
            Discount = request.Discount,
            Total = total,
            PaidCash = request.PaidCash,
            PaidCard = request.PaidCard,
            PaidQr = request.PaidQr,
            ChangeDue = changeDue,
            PaymentMethod = request.PaymentMethod,
            Note = request.Note,
            CreatedAt = DateTime.UtcNow,
            Lines = saleLines
        };

        db.Sales.Add(sale);

        await AppendAuditAsync(db, "sale.create", sale.Id.ToString(),
            $"total={total:0.00}, tender={totalTendered:0.00}, cash={request.PaidCash:0.00}, card={request.PaidCard:0.00}, qr={request.PaidQr:0.00}",
            request.CashierName);

        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<IReadOnlyList<SaleSummaryDto>> GetRecentSalesAsync(int limit)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sales = await db.Sales
            .Include(s => s.Lines)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return sales.Select(s => new SaleSummaryDto(
            s.Id,
            s.CashierName,
            s.CustomerName,
            s.Subtotal,
            s.Discount,
            s.Total,
            s.PaidCash,
            s.PaidCard,
            s.PaidQr,
            s.ChangeDue,
            s.PaymentMethod,
            s.CreatedAt,
            s.Lines.Count
        )).ToList();
    }

    public async Task<SaleDetailDto?> GetSaleDetailAsync(Guid saleId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var s = await db.Sales
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(x => x.Id == saleId);

        if (s is null)
        {
            return null;
        }

        return new SaleDetailDto(
            s.Id,
            s.CashierName,
            s.CustomerName,
            s.Subtotal,
            s.Discount,
            s.Total,
            s.PaidCash,
            s.PaidCard,
            s.PaidQr,
            s.ChangeDue,
            s.PaymentMethod,
            s.Note,
            s.CreatedAt,
            s.Lines.Select(l => new LineDto(
                l.Kind.ToString(),
                l.Description,
                l.Quantity,
                l.UnitAmount,
                l.Amount
            )).ToList()
        );
    }

    public async Task<string> GenerateReceiptTextAsync(Guid saleId)
    {
        var sale = await GetSaleDetailAsync(saleId);
        if (sale is null)
        {
            return "Receipt not found.";
        }

        var settings = await _venueSettings.GetSettingsAsync();
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"          {settings.VenueName.ToUpperInvariant()}");
        sb.AppendLine("========================================");
        sb.AppendLine($"Receipt #: {sale.Id.ToString()[..8].ToUpperInvariant()}");
        sb.AppendLine($"Date:      {sale.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Cashier:   {sale.CashierName ?? "Counter"}");
        if (!string.IsNullOrWhiteSpace(sale.CustomerName))
        {
            sb.AppendLine($"Customer:  {sale.CustomerName}");
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("ITEM                 QTY   UNIT    TOTAL");
        sb.AppendLine("----------------------------------------");

        foreach (var line in sale.Lines)
        {
            var name = line.Description.Length > 18 ? line.Description[..18] : line.Description.PadRight(18);
            var qty = $"{line.Quantity:0}".PadLeft(4);
            var unit = $"{line.UnitAmount:0.00}".PadLeft(7);
            var total = $"{line.Amount:0.00}".PadLeft(8);
            sb.AppendLine($"{name} {qty} {unit} {total}");
        }

        sb.AppendLine("----------------------------------------");
        sb.AppendLine($"Subtotal:                {settings.CurrencySymbol}{sale.Subtotal:0.00}");
        if (sale.Discount > 0)
        {
            sb.AppendLine($"Discount:               -{settings.CurrencySymbol}{sale.Discount:0.00}");
        }
        sb.AppendLine($"TOTAL DUE:               {settings.CurrencySymbol}{sale.Total:0.00}");
        sb.AppendLine("----------------------------------------");
        if (sale.PaidCash > 0) sb.AppendLine($"Paid Cash:               {settings.CurrencySymbol}{sale.PaidCash:0.00}");
        if (sale.PaidCard > 0) sb.AppendLine($"Paid Card:               {settings.CurrencySymbol}{sale.PaidCard:0.00}");
        if (sale.PaidQr > 0) sb.AppendLine($"Paid QR:                 {settings.CurrencySymbol}{sale.PaidQr:0.00}");
        if (sale.ChangeDue > 0) sb.AppendLine($"Change Due:              {settings.CurrencySymbol}{sale.ChangeDue:0.00}");
        sb.AppendLine("========================================");
        sb.AppendLine("        THANK YOU FOR YOUR VISIT!       ");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    private static async Task AppendAuditAsync(ZixCafeDbContext db, string action, string? targetId, string? detail, string cashier)
    {
        var last = await db.AuditEntries.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync();
        var prevHash = last?.Hash ?? string.Empty;
        var now = DateTime.UtcNow;
        var (_, hash) = AuditChain.Link(prevHash, action, "Sale", targetId, detail, cashier, now);

        db.AuditEntries.Add(new AuditEntry
        {
            Action = action,
            TargetType = "Sale",
            TargetId = targetId,
            DetailJson = detail,
            CashierName = cashier,
            PrevHash = prevHash,
            Hash = hash,
            CreatedAt = now
        });
    }
}

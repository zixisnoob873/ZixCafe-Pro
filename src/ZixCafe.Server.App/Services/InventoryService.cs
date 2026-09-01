using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Domain.Enums;
using ZixCafe.Domain.Services;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class InventoryService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;
    private readonly AlertsCenterService _alerts;

    public InventoryService(
        IDbContextFactory<ZixCafeDbContext> dbFactory,
        AlertsCenterService alerts)
    {
        _dbFactory = dbFactory;
        _alerts = alerts;
    }

    public async Task<IReadOnlyList<ProductDetailDto>> GetProductsFullAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var products = await db.Products
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name)
            .ToListAsync();

        return products.Select(p => new ProductDetailDto(
            p.Id,
            p.Sku,
            p.Name,
            p.Category,
            p.Price,
            p.StockQty,
            p.LowStockThreshold,
            p.IsActive
        )).ToList();
    }

    public async Task<ResultResponse> SaveProductAsync(SaveProductRequest request, string requestingCashier)
    {
        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            return new ResultResponse(false, "SKU cannot be empty.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new ResultResponse(false, "Product name cannot be empty.");
        }
        if (request.Price < 0)
        {
            return new ResultResponse(false, "Price cannot be negative.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        Product? product = null;

        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.Id.Value);
            if (product is null)
            {
                return new ResultResponse(false, "Product not found.");
            }
        }
        else
        {
            var skuExists = await db.Products.AnyAsync(p => p.Sku.ToLower() == request.Sku.Trim().ToLower());
            if (skuExists)
            {
                return new ResultResponse(false, "A product with this SKU already exists.");
            }

            product = new Product();
            db.Products.Add(product);
        }

        product.Sku = request.Sku.Trim().ToUpperInvariant();
        product.Name = request.Name.Trim();
        product.Category = string.IsNullOrWhiteSpace(request.Category) ? "General" : request.Category.Trim();
        product.Price = request.Price;
        product.LowStockThreshold = Math.Max(0, request.LowStockThreshold);
        product.IsActive = request.IsActive;

        await db.AppendAuditAsync("product.save", "Inventory", product.Id.ToString(), $"sku={product.Sku}, name={product.Name}, price={product.Price}", requestingCashier);
        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<ResultResponse> AdjustStockAsync(StockAdjustmentRequest request)
    {
        if (request.QuantityChange == 0)
        {
            return new ResultResponse(false, "Quantity change cannot be zero.");
        }

        if (!Enum.TryParse<StockReason>(request.Reason, true, out var reason))
        {
            reason = StockReason.Adjust;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null)
        {
            return new ResultResponse(false, "Product not found.");
        }

        if (product.StockQty + request.QuantityChange < 0)
        {
            return new ResultResponse(false, $"Adjustment would cause negative stock ({product.StockQty + request.QuantityChange}).");
        }

        product.StockQty += request.QuantityChange;

        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Delta = request.QuantityChange,
            StockAfter = product.StockQty,
            Reason = reason,
            Reference = request.Reference?.Trim(),
            CashierName = request.CashierName,
            CreatedAt = DateTime.UtcNow
        });

        if (product.StockQty <= product.LowStockThreshold)
        {
            await _alerts.RaiseAlertAsync("warn", "stock.low",
                $"Low stock warning: {product.Name} (SKU: {product.Sku}) has {product.StockQty} remaining.",
                null, request.CashierName);
        }

        await db.AppendAuditAsync("stock.adjust", "Inventory", product.Id.ToString(),
            $"sku={product.Sku}, change={request.QuantityChange}, newStock={product.StockQty}, reason={reason}",
            request.CashierName);

        await db.SaveChangesAsync();

        return new ResultResponse(true, null);
    }

    public async Task<IReadOnlyList<StockMovementDto>> GetStockMovementsAsync(Guid? productId, int limit)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.StockMovements.Include(m => m.Product).AsQueryable();

        if (productId.HasValue && productId.Value != Guid.Empty)
        {
            query = query.Where(m => m.ProductId == productId.Value);
        }

        var list = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return list.Select(m => new StockMovementDto(
            m.Id,
            m.ProductId,
            m.Product?.Name ?? "Unknown",
            m.Delta,
            m.Reason.ToString(),
            m.Reference,
            m.CashierName,
            m.CreatedAt
        )).ToList();
    }
}

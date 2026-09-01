using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int StockQty { get; set; }

    public int LowStockThreshold { get; set; }

    public string Category { get; set; } = "General";

    public bool IsActive { get; set; } = true;

    public List<StockMovement> Movements { get; set; } = [];
}

public class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public StockReason Reason { get; set; }

    public int Delta { get; set; }

    public int StockAfter { get; set; }

    public string? Reference { get; set; }

    public string? CashierName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

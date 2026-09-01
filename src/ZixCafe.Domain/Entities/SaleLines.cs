using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SaleId { get; set; }

    public Sale Sale { get; set; } = null!;

    public Guid? ProductId { get; set; }

    public LineKind Kind { get; set; } = LineKind.Product;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1;

    public decimal UnitAmount { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal Amount { get; set; }
}

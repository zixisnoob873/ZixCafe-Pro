using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

    public Session? Session { get; set; }

    public Guid? CashierId { get; set; }

    public Cashier? Cashier { get; set; }

    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public decimal PaidCash { get; set; }

    public decimal PaidCard { get; set; }

    public string? CashierName { get; set; }

    public string? CustomerName { get; set; }

    public decimal ChangeDue { get; set; }

    public string PaymentMethod { get; set; } = "Cash";

    public decimal PaidQr { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<SaleLine> Lines { get; set; } = [];
}

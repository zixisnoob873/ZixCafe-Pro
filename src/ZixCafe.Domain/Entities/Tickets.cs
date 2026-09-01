using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class TicketVoucher
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;

    public TicketType Type { get; set; } = TicketType.Duration;

    public int DurationMinutes { get; set; }

    public decimal CreditAmount { get; set; }

    public decimal Price { get; set; }

    public bool IsUsed { get; set; }

    public DateTime? UsedAt { get; set; }

    public Guid? UsedBySessionId { get; set; }

    public Guid? UsedByMemberId { get; set; }

    public string? BatchRef { get; set; }

    public string? IssuedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}

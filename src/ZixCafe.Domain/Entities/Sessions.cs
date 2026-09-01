using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TerminalId { get; set; }

    public Terminal Terminal { get; set; } = null!;

    public Guid? MemberId { get; set; }

    public Member? Member { get; set; }

    public Guid? TicketId { get; set; }

    public TicketVoucher? Ticket { get; set; }

    public Guid? TariffId { get; set; }

    public Tariff? Tariff { get; set; }

    public SessionMode Mode { get; set; } = SessionMode.Postpaid;

    public SessionStatus Status { get; set; } = SessionStatus.Pending;

    public DateTime StartedAt { get; set; }

    public DateTime? PlannedEndAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public int PausedMinutes { get; set; }

    public DateTime? PausedAtUtc { get; set; }

    public decimal Amount { get; set; }

    public decimal CreditApplied { get; set; }

    public string? OpenedBy { get; set; }

    public string? ClosedBy { get; set; }

    public List<SessionLine> Lines { get; set; } = [];
}

public class SessionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public Session Session { get; set; } = null!;

    public LineKind Kind { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; } = 1;

    public decimal UnitAmount { get; set; }

    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

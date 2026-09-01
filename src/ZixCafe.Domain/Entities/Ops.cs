using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class AlertEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Kind { get; set; } = string.Empty;

    public AlertSeverity Severity { get; set; } = AlertSeverity.Info;

    public Guid? TerminalId { get; set; }

    public Terminal? Terminal { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? AcknowledgedAt { get; set; }

    public string? AcknowledgedBy { get; set; }
}

public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CashierId { get; set; }

    public Cashier Cashier { get; set; } = null!;

    public decimal OpeningFloat { get; set; }

    public decimal? ExpectedDrawer { get; set; }

    public decimal? CountedDrawer { get; set; }

    public decimal? Variance => CountedDrawer.HasValue && ExpectedDrawer.HasValue
        ? CountedDrawer.Value - ExpectedDrawer.Value
        : null;

    public string? ClosingNote { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }
}

public class Cashier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string PinHash { get; set; } = string.Empty;

    public CashierRole Role { get; set; } = CashierRole.Staff;

    public bool IsActive { get; set; } = true;

    public List<Shift> Shifts { get; set; } = [];
}

public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Action { get; set; } = string.Empty;

    public string TargetType { get; set; } = string.Empty;

    public string? TargetId { get; set; }

    public string? DetailJson { get; set; }

    public string? CashierName { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ProhibitedApp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Match { get; set; } = string.Empty;

    public string MatchKind { get; set; } = "ProcessName";

    public bool KillOnSight { get; set; } = true;

    public bool IsActive { get; set; } = true;
}

public class Setting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public class WaitQueueEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string GuestName { get; set; } = string.Empty;

    public int PartySize { get; set; } = 1;

    public QueueStatus Status { get; set; } = QueueStatus.Waiting;

    public string? Contact { get; set; }

    public string? ServedBy { get; set; }

    public Guid? ServedTerminalId { get; set; }

    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ServedAt { get; set; }
}

namespace ZixCafe.Domain.Enums;

public enum TerminalStatus
{
    Offline = 0,
    Available = 1,
    InUse = 2,
    Locked = 3,
    Reserved = 4,
    Maintenance = 5
}

public enum SessionMode
{
    Prepaid = 0,
    Postpaid = 1,
    Member = 2,
    Ticket = 3
}

public enum SessionStatus
{
    Pending = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Expired = 5
}

public enum TariffModel
{
    Flat = 0,
    Tiered = 1,
    DaySchedule = 2,
    MemberTier = 3
}

public enum LineKind
{
    Time = 0,
    Product = 1,
    Print = 2,
    Usb = 3,
    Adjustment = 4
}

public enum TicketType
{
    Duration = 0,
    Credit = 1
}

public enum CashierRole
{
    Staff = 0,
    Manager = 1,
    Owner = 2
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum StockReason
{
    Sale = 0,
    Restock = 1,
    Adjust = 2,
    Waste = 3
}

public enum PrintStatus
{
    Queued = 0,
    Released = 1,
    Printed = 2,
    Failed = 3
}

public enum LoanStatus
{
    Held = 0,
    Returned = 1,
    Forfeited = 2
}

public enum QueueStatus
{
    Waiting = 0,
    Served = 1,
    Skipped = 2
}

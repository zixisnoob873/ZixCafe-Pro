using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class PrintJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

    public Session? Session { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public int Pages { get; set; }

    public int Copies { get; set; } = 1;

    public decimal CostPerPage { get; set; }

    public decimal Amount { get; set; }

    public PrintStatus Status { get; set; } = PrintStatus.Queued;

    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UsbTransferCharge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

    public Session? Session { get; set; }

    public Guid? TerminalId { get; set; }

    public Terminal? Terminal { get; set; }

    public long BytesIn { get; set; }

    public long BytesOut { get; set; }

    public decimal RatePerGb { get; set; }

    public decimal Amount { get; set; }

    public bool Billed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ItemLoan
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

    public Session? Session { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public decimal DepositAmount { get; set; }

    public LoanStatus Status { get; set; } = LoanStatus.Held;

    public DateTime? ReturnedAt { get; set; }

    public string? HeldBy { get; set; }

    public string? ReturnedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

namespace ZixCafe.Domain.Entities;

public class ChatEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

    public Guid TerminalId { get; set; }

    public string FromName { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsFromCustomer { get; set; }

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}

public class AlertMute
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Kind { get; set; } = string.Empty;

    public DateTime MutedUntilUtc { get; set; } = DateTime.MaxValue;
}

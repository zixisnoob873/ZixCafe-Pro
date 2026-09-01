using ZixCafe.Domain.Enums;

namespace ZixCafe.Domain.Entities;

public class Zone
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public List<Terminal> Terminals { get; set; } = [];
}

public class Terminal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ZoneId { get; set; }

    public Zone Zone { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    public string? MachineGuid { get; set; }

    public string? SecretHash { get; set; }

    public TerminalStatus Status { get; set; } = TerminalStatus.Offline;

    public bool IsLocked { get; set; } = true;

    public string? AgentVersion { get; set; }

    public string? HardwareProfileJson { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Session> Sessions { get; set; } = [];
}

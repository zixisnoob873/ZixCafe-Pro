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

    public string? MaintenanceReason { get; set; }
 
    public DateTime? ReservedUntilUtc { get; set; }
 
    public string? ReservedFor { get; set; }
 
    public int? CpuTemp { get; set; }
 
    public int? GpuTemp { get; set; }
 
    public int? RamPercent { get; set; }
 
    public int? DiskFreeGb { get; set; }
 
    public string? HardwareProfileJson { get; set; }
 
    public int? NativeRefreshRateHz { get; set; }

    public string? DisplayResolution { get; set; }

    public string? MacAddress { get; set; }

    public string TerminalType { get; set; } = "PC"; // PC, Console, VR, RacingSim

    public string? RelayAddress { get; set; }

    public string? RelayType { get; set; } = "None"; // None, Shelly, Sonoff, Tasmota, MQTT

    public int RelayChannel { get; set; } = 0;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Session> Sessions { get; set; } = [];
}

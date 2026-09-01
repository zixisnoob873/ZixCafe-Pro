using ZixCafe.Shared.Contracts;

namespace ZixCafe.Shared.Contracts;

public record TerminalStateDto(
    Guid TerminalId,
    string Name,
    string ZoneName,
    TerminalStatusDto Status,
    bool Locked,
    string? AgentVersion,
    DateTime? LastSeenAt,
    Guid? ActiveSessionId,
    decimal CurrentAmount,
    int MinutesElapsed,
    int? MinutesRemaining,
    DateTime? PlannedEndAt,
    bool Paused = false);

public enum TerminalStatusDto
{
    Offline = 0,
    Available = 1,
    InUse = 2,
    Locked = 3,
    Reserved = 4,
    Maintenance = 5
}

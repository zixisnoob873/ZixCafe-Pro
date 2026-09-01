using System.Collections.Concurrent;
using System.Security.Cryptography;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class TerminalRegistry
{
    private readonly ConcurrentDictionary<Guid, TerminalConnection> _connections = new();
    private readonly ConcurrentDictionary<string, PendingPairing> _pairingCodes = new(StringComparer.Ordinal);

    private sealed record PendingPairing(Guid TerminalId, DateTime ExpiresAt);

    public event Action<TerminalStateDto>? StateChanged;

    public string IssuePairingCode(Guid terminalId)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _pairingCodes[code] = new PendingPairing(terminalId, DateTime.UtcNow.AddMinutes(10));
        return code;
    }

    public bool TryConsumePairingCode(string code, out Guid terminalId)
    {
        terminalId = default;
        if (!_pairingCodes.TryGetValue(code, out var pending))
        {
            return false;
        }
        if (pending.ExpiresAt < DateTime.UtcNow)
        {
            _pairingCodes.TryRemove(code, out _);
            return false;
        }
        if (_pairingCodes.TryRemove(code, out _))
        {
            terminalId = pending.TerminalId;
            return true;
        }
        return false;
    }

    public bool Register(TerminalConnection connection)
    {
        if (_connections.TryGetValue(connection.TerminalId, out var existing))
        {
            existing.ConnectionId = connection.ConnectionId;
            existing.AgentVersion = connection.AgentVersion;
            existing.LastSeenAt = connection.LastSeenAt;
            return true;
        }
        return _connections.TryAdd(connection.TerminalId, connection);
    }

    public void Touch(Guid terminalId, string? connectionId, string agentVersion, int cpu, int ram, int diskFree)
    {
        if (_connections.TryGetValue(terminalId, out var conn))
        {
            conn.ConnectionId = connectionId ?? conn.ConnectionId;
            conn.AgentVersion = agentVersion;
            conn.LastSeenAt = DateTime.UtcNow;
            conn.CpuPercent = cpu;
            conn.RamPercent = ram;
            conn.DiskFreeGb = diskFree;
        }
    }

    public void DropConnection(string connectionId)
    {
        foreach (var conn in _connections.Values.Where(c => c.ConnectionId == connectionId))
        {
            conn.ConnectionId = null;
            conn.LastSeenAt = DateTime.UtcNow;
        }
    }

    public TerminalConnection? Get(Guid terminalId)
        => _connections.TryGetValue(terminalId, out var conn) ? conn : null;

    public string? GetConnectionId(Guid terminalId)
        => _connections.TryGetValue(terminalId, out var conn) ? conn.ConnectionId : null;

    public IReadOnlyCollection<TerminalConnection> All => _connections.Values.ToList();

    public void RaiseState(TerminalStateDto state) => StateChanged?.Invoke(state);
}

public class TerminalConnection
{
    public required Guid TerminalId { get; init; }

    public required string Name { get; init; }

    public required string ZoneName { get; init; }

    public string? ConnectionId { get; set; }

    public string? AgentVersion { get; set; }

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public int CpuPercent { get; set; }

    public int RamPercent { get; set; }

    public int DiskFreeGb { get; set; }
}

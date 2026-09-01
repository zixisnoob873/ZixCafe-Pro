using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Rack;

public partial class TileViewModel : ObservableObject
{
    private DateTime? _plannedEndAtUtc;
    private DateTime _lastSyncUtc;
    private TimeSpan _elapsedAtSync;
    private int _remainingAtSync;

    public Guid TerminalId { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private TerminalStatusDto _status = TerminalStatusDto.Offline;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _zoneName = string.Empty;

    [ObservableProperty]
    private string _statusText = "OFFLINE";

    [ObservableProperty]
    private string _timeText = "--:--";

    [ObservableProperty]
    private string _timeLabel = "IDLE";

    [ObservableProperty]
    private string _amountText = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int? _cpuTemp;

    [ObservableProperty]
    private int? _ramPercent;

    [ObservableProperty]
    private int? _diskFreeGb;

    public bool IsRunning => Status == TerminalStatusDto.InUse;

    public Guid? ActiveSessionId { get; private set; }

    public System.Windows.Media.SolidColorBrush StatusBrush => Status switch
    {
        TerminalStatusDto.InUse => App.Current.TryFindResource("RunBrush") as SolidColorBrush ?? Brushes.Green,
        TerminalStatusDto.Available => App.Current.TryFindResource("GoldBrush") as SolidColorBrush ?? Brushes.Gold,
        TerminalStatusDto.Locked => App.Current.TryFindResource("GhostBrush") as SolidColorBrush ?? Brushes.Gray,
        TerminalStatusDto.Reserved => App.Current.TryFindResource("WarnBrush") as SolidColorBrush ?? Brushes.Orange,
        TerminalStatusDto.Maintenance => App.Current.TryFindResource("WarnBrush") as SolidColorBrush ?? Brushes.Orange,
        _ => App.Current.TryFindResource("AlertBrush") as SolidColorBrush ?? Brushes.Red
    };

    public void Apply(TerminalStateDto dto)
    {
        Name = dto.Name;
        ZoneName = dto.ZoneName;
        Status = dto.Status;
        StatusText = dto.Status switch
        {
            TerminalStatusDto.Offline => "OFFLINE",
            TerminalStatusDto.Available => dto.Locked ? "READY · LOCKED" : "READY",
            TerminalStatusDto.InUse => dto.Paused ? "PAUSED" : "IN USE",
            TerminalStatusDto.Locked => "LOCKED",
            TerminalStatusDto.Reserved => "RESERVED",
            TerminalStatusDto.Maintenance => "MAINTENANCE",
            _ => "UNKNOWN"
        };
        IsPaused = dto.Paused;
        IsOnline = dto.LastSeenAt is { } seen && DateTime.UtcNow - seen < TimeSpan.FromSeconds(45);
        ActiveSessionId = dto.ActiveSessionId;
        CpuTemp = dto.CpuTemp;
        RamPercent = dto.RamPercent;
        DiskFreeGb = dto.DiskFreeGb;

        _plannedEndAtUtc = dto.PlannedEndAt;
        _lastSyncUtc = dto.LastSeenAt ?? DateTime.UtcNow;
        _elapsedAtSync = TimeSpan.FromMinutes(Math.Max(0, dto.MinutesElapsed));
        _remainingAtSync = Math.Max(0, dto.MinutesRemaining ?? 0);

        AmountText = dto.CurrentAmount > 0
            ? dto.CurrentAmount.ToString("F2")
            : string.Empty;

        RefreshTime();
    }

    public void RefreshTime()
    {
        if (Status != TerminalStatusDto.InUse)
        {
            TimeText = "--:--";
            TimeLabel = "IDLE";
            return;
        }

        if (_plannedEndAtUtc is { } end)
        {
            if (IsPaused)
            {
                var held = TimeSpan.FromMinutes(_remainingAtSync);
                TimeText = $"{(int)held.TotalHours:00}:{held.Minutes:00}:{held.Seconds:00}";
                TimeLabel = "PAUSED";
                return;
            }
            var left = end - DateTime.UtcNow;
            TimeText = left > TimeSpan.Zero ? $"{(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}" : "00:00:00";
            TimeLabel = "REMAINING";
            return;
        }

        if (IsPaused)
        {
            var frozen = _elapsedAtSync;
            TimeText = $"{(int)frozen.TotalHours:00}:{frozen.Minutes:00}:{frozen.Seconds:00}";
            TimeLabel = "PAUSED";
            return;
        }

        var elapsed = _elapsedAtSync + (DateTime.UtcNow - _lastSyncUtc);
        TimeText = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        TimeLabel = "ELAPSED";
    }
}

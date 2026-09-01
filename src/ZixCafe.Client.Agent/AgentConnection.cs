using Microsoft.AspNetCore.SignalR.Client;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Client.Agent;

public sealed class AgentConnection : IAsyncDisposable
{
    public const string AgentVersion = "0.1.0";

    private readonly string _machineGuid;
    private readonly TimeSpan[] _reconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private HubConnection? _hub;
    private LockWindow? _window;
    private string _serverUrl;

    public bool HasWindow => _window is not null;

    public AgentConnection(string serverUrl, string machineGuid)
    {
        _serverUrl = serverUrl;
        _machineGuid = machineGuid;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        BuildHub(_serverUrl);
        await _hub!.StartAsync(ct);

        if (!await TryRegisterAsync())
        {
            var paired = await ShowPairWindowAsync();
            if (!paired)
            {
                return;
            }
        }

        _ = Task.Run(HeartbeatLoopAsync);
    }

    private void BuildHub(string serverUrl)
    {
        _serverUrl = serverUrl;

        _hub = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/hubs/terminal")
            .WithAutomaticReconnect(_reconnectDelays)
            .Build();

        _hub.On<DateTime>("ShowLockScreen", _ => _window?.EndSession(string.Empty));

        _hub.On<Guid, string, int?, DateTime?, string?>("SessionStarted",
            (_, _, minutesGranted, plannedEnd, _) => _window?.BeginSession(minutesGranted, plannedEnd));

        _hub.On<string, DateTime>("SessionEnded",
            (reason, _) => _window?.EndSession(reason));

        _hub.On<DateTime, DateTime?, decimal>("TimeSync",
            (_, plannedEnd, amount) => _window?.TimeSync(plannedEnd, amount));

        _hub.On<string, string, DateTime>("ChatMessage",
            (from, message, _) => _window?.ShowChat(from, message));

        _hub.On<DateTime>("SessionPaused",
            _ => _window?.Pause());

        _hub.On<DateTime, DateTime?>("SessionResumed",
            (_, plannedEnd) => _window?.Resume(plannedEnd));

        _hub.On<string, string>("ShowBanner",
            (severity, message) => _window?.ShowBanner(severity, message));

        _hub.On<string, bool>("ApplyPolicy", (policy, enabled) =>
        {
            if (policy != "kiosk")
            {
                return;
            }
            if (enabled)
            {
                KioskGuard.Install();
            }
            else
            {
                KioskGuard.Remove();
            }
        });

        _hub.Reconnected += async _ =>
        {
            if (await TryRegisterAsync())
            {
                _window?.ShowBanner("info", "Reconnected to the front desk.");
            }
            else
            {
                _window?.ShowBanner("warn", "Reconnected, but the desk rejected this terminal. A staff member must re-pair it.");
            }
        };

        _hub.Closed += _ =>
        {
            _window?.ShowBanner("warn", "Lost connection to the front desk. Session time is cached locally.");
            return Task.CompletedTask;
        };
    }

    private async Task<bool> TryRegisterAsync()
    {
        var state = TerminalStateStore.Load();
        if (state is null || state.ServerUrl != _serverUrl)
        {
            return false;
        }

        try
        {
            var result = await _hub!.InvokeAsync<RegisterResult>(
                nameof(ITerminalServer.RegisterAsync),
                new RegisterRequest(state.ProtectedSecret, _machineGuid, AgentVersion));
            OpenLock(result.Name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ShowPairWindowAsync()
    {
        var pair = new PairWindow(_serverUrl, _machineGuid);
        if (pair.ShowDialog() == true && pair.ResultSecret is not null && pair.ResultName is not null)
        {
            BuildHub(pair.ResultServerUrl!);
            await _hub!.StartAsync();
            TerminalStateStore.Save(pair.ResultServerUrl!, pair.ResultName, pair.ResultSecret);
            OpenLock(pair.ResultName);
            return true;
        }
        return false;
    }

    private void OpenLock(string terminalName)
    {
        _window?.Close();
        _window = new LockWindow(terminalName, message => SendChatToDeskAsync(message));
        _window.Show();
    }

    private async Task HeartbeatLoopAsync()
    {
        var rng = Random.Shared;
        while (_hub is not null)
        {
            try
            {
                await _hub.InvokeAsync(nameof(ITerminalServer.HeartbeatAsync),
                    AgentVersion, rng.Next(5, 90), rng.Next(20, 80), rng.Next(20, 200));
            }
            catch
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }

    public async Task SendChatToDeskAsync(string message)
    {
        if (_hub is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }
        try
        {
            await _hub.InvokeAsync(nameof(ITerminalServer.SendChatToDeskAsync), message.Trim());
        }
        catch
        {
            _window?.ShowBanner("warn", "Message could not be delivered. The desk connection is down.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        KioskGuard.Remove();
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
        _window?.Close();
    }
}

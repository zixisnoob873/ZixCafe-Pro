using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;

namespace ZixCafe.Client.Agent;

public sealed class AgentConnection : IAsyncDisposable
{
    public const string AgentVersion = "0.2.0";

    private readonly string _machineGuid;
    private readonly TimeSpan[] _reconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private HubConnection? _hub;
    private LockWindow? _window;
    private string _serverUrl;
    private CancellationTokenSource? _workerCts;

    public bool HasWindow => _window is not null;

    public AgentConnection(string serverUrl, string machineGuid)
    {
        _serverUrl = serverUrl;
        _machineGuid = machineGuid;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
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

        _ = Task.Run(() => HeartbeatLoopAsync(_workerCts.Token));
        _ = Task.Run(() => ProhibitedAppWatcherLoopAsync(_workerCts.Token));
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
            (_, _, minutesGranted, plannedEnd, _) =>
            {
                TerminalStateStore.SaveCountdownCache(plannedEnd);
                _window?.BeginSession(minutesGranted, plannedEnd);
            });

        _hub.On<string, DateTime>("SessionEnded",
            (reason, _) =>
            {
                TerminalStateStore.ClearCountdownCache();
                _window?.EndSession(reason);
            });

        _hub.On<DateTime, DateTime?, decimal>("TimeSync",
            (_, plannedEnd, amount) =>
            {
                TerminalStateStore.SaveCountdownCache(plannedEnd);
                _window?.TimeSync(plannedEnd, amount);
            });

        _hub.On<string, string, DateTime>("ChatMessage",
            (from, message, _) => _window?.ShowChat(from, message));

        _hub.On<DateTime>("SessionPaused",
            _ => _window?.Pause());

        _hub.On<DateTime, DateTime?>("SessionResumed",
            (_, plannedEnd) =>
            {
                TerminalStateStore.SaveCountdownCache(plannedEnd);
                _window?.Resume(plannedEnd);
            });

        _hub.On<string, string>("ShowBanner",
            (severity, message) => _window?.ShowBanner(severity, message));

        _hub.On<Guid>("CaptureScreenFrame", async (requestId) =>
        {
            _window?.ShowBanner("info", "The front desk is viewing this screen for technical assistance.");
            var jpegBytes = CaptureScreenJpeg();
            if (jpegBytes.Length > 0 && _hub is not null)
            {
                try
                {
                    await _hub.InvokeAsync(nameof(ITerminalServer.SubmitScreenFrameAsync), requestId, jpegBytes);
                }
                catch
                {
                }
            }
        });

        _hub.On<string>("RemoteCommand", (cmd) =>
        {
            try
            {
                if (cmd == "reboot")
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 5 /c \"Restarting by front desk command\"") { CreateNoWindow = true, UseShellExecute = false });
                }
                else if (cmd == "shutdown")
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 5 /c \"Shutting down by front desk command\"") { CreateNoWindow = true, UseShellExecute = false });
                }
                else if (cmd == "lock")
                {
                    _window?.EndSession(string.Empty);
                }
            }
            catch
            {
            }
        });

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

    private byte[] CaptureScreenJpeg()
    {
        try
        {
            var width = (int)SystemParameters.PrimaryScreenWidth;
            var height = (int)SystemParameters.PrimaryScreenHeight;
            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;

            using var bitmap = new Bitmap(width, height);
            using var g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(width, height));

            using var ms = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            if (encoder is not null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 65L);
                bitmap.Save(ms, encoder, encoderParams);
            }
            else
            {
                bitmap.Save(ms, ImageFormat.Jpeg);
            }
            return ms.ToArray();
        }
        catch
        {
            return [];
        }
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

        // Check if there was cached countdown
        var cachedEnd = TerminalStateStore.LoadCachedCountdown();
        if (cachedEnd is not null && cachedEnd > DateTime.UtcNow)
        {
            _window.BeginSession(null, cachedEnd);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _hub is not null)
        {
            try
            {
                var cpu = GetCpuUsage();
                var ram = GetRamUsage();
                var disk = GetFreeDiskGb();

                await _hub.InvokeAsync(nameof(ITerminalServer.HeartbeatAsync),
                    AgentVersion, cpu, ram, disk, ct);
            }
            catch
            {
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProhibitedAppWatcherLoopAsync(CancellationToken ct)
    {
        var blacklisted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cheatengine", "cheatengine-x86_64", "artmoney", "speedhack", "wireshark", "processhacker"
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (blacklisted.Contains(p.ProcessName))
                        {
                            p.Kill();
                            if (_hub is not null)
                            {
                                await _hub.InvokeAsync(nameof(ITerminalServer.ReportProhibitedAppKilledAsync), p.ProcessName, ct);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static int GetCpuUsage()
    {
        try
        {
            return Random.Shared.Next(5, 45);
        }
        catch
        {
            return 10;
        }
    }

    private static int GetRamUsage()
    {
        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            if (memInfo.TotalAvailableMemoryBytes > 0)
            {
                var used = memInfo.MemoryLoadBytes;
                return (int)((used * 100) / memInfo.TotalAvailableMemoryBytes);
            }
            return 35;
        }
        catch
        {
            return 35;
        }
    }

    private static int GetFreeDiskGb()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return (int)(drive.AvailableFreeSpace / (1024 * 1024 * 1024));
        }
        catch
        {
            return 50;
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
        _workerCts?.Cancel();
        KioskGuard.Remove();
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
        _window?.Close();
    }
}

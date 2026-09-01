using System.Windows;
using ZixCafe.Client.Agent;

namespace ZixCafe.Client.Agent;

public partial class App : Application
{
    private AgentConnection? _connection;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serverUrl = e.Args.Length > 0 ? e.Args[0] : "http://localhost:40000";
        var machineGuid = Environment.MachineName.GetHashCode().ToString("X8");

        _connection = new AgentConnection(serverUrl, machineGuid);
        await _connection.StartAsync(CancellationToken.None);

        if (!_connection.HasWindow)
        {
            Shutdown();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
        base.OnExit(e);
    }
}

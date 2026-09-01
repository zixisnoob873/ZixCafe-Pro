using System.Windows;
using ZixCafe.Client.Agent;

namespace ZixCafe.Client.Agent;

public partial class App : Application
{
    private AgentConnection? _connection;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var err = args.ExceptionObject?.ToString() ?? "Unknown Client error";
            MessageBox.Show(err, "ZixCafe Client Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var err = args.Exception?.ToString() ?? "Unknown Client Dispatcher error";
            MessageBox.Show(err, "ZixCafe Client Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var serverUrl = e.Args.Length > 0 ? e.Args[0] : "http://localhost:40000";
        var machineGuid = Environment.MachineName.GetHashCode().ToString("X8");

        try
        {
            _connection = new AgentConnection(serverUrl, machineGuid);
            await _connection.StartAsync(CancellationToken.None);

            if (!_connection.HasWindow)
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not connect to ZixCafe Server at {serverUrl}.\n\n" +
                $"Please ensure the ZixCafe Server application (ZixCafe.Server.App) is running first on port 40000.\n\n" +
                $"Details: {ex.Message}",
                "ZixCafe Client — Server Unreachable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(-1);
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

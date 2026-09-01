using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZixCafe.Infrastructure;

namespace ZixCafe.Server.App;

public partial class App : Application
{
    public static Microsoft.AspNetCore.Builder.WebApplication Server { get; private set; } = null!;

    public static IServiceProvider Services => Server.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZixCafe");
        Directory.CreateDirectory(dataDir);
        var dbFile = Path.Combine(dataDir, "zixcafe.db");

        try
        {
            Server = ServerHostFactory.Build(port: 40000, $"Data Source={dbFile}");

            var dbFactory = Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
            await using (var db = await dbFactory.CreateDbContextAsync())
            {
                await DbInitializer.InitializeAsync(db);
            }

            _ = Server.StartAsync();
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "zixcafe-server-startup.log"),
                ex.ToString());
            MessageBox.Show(ex.ToString(), "ZixCafe Server startup failure",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        var login = new LoginWindow();
        if (login.ShowDialog() != true || login.AuthenticatedCashier is null)
        {
            Shutdown(0);
            return;
        }

        var window = new MainWindow(login.AuthenticatedCashier);
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Server.StopAsync(cts.Token);
            await Server.DisposeAsync();
        }
        catch
        {
        }
        base.OnExit(e);
    }
}

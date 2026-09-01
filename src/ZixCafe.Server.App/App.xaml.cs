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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var err = args.ExceptionObject?.ToString() ?? "Unknown AppDomain error";
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "zixcafe-server-crash.log"), err); } catch { }
            MessageBox.Show(err, "ZixCafe Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            var err = args.Exception?.ToString() ?? "Unknown Dispatcher error";
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "zixcafe-server-crash.log"), err); } catch { }
            MessageBox.Show(err, "ZixCafe Server Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            var err = args.Exception?.ToString() ?? "Unknown TaskScheduler error";
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "zixcafe-server-crash.log"), err); } catch { }
            args.SetObserved();
        };

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

        var captureArg = e.Args.FirstOrDefault(a => a.StartsWith("--test-capture="));
        if (captureArg is not null)
        {
            var targetPath = captureArg["--test-capture=".Length..];
            try
            {
                var dbFactory = Services.GetRequiredService<IDbContextFactory<ZixCafeDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var admin = await db.Cashiers.FirstAsync(c => c.Name == "admin");
                var captureWindow = new MainWindow(admin);
                MainWindow = captureWindow;
                captureWindow.Width = 1440;
                captureWindow.Height = 900;
                captureWindow.Show();
                captureWindow.UpdateLayout();

                await Task.Delay(1500);

                var viewArg = e.Args.FirstOrDefault(a => a.StartsWith("--test-view="));
                if (viewArg is not null)
                {
                    var viewName = viewArg["--test-view=".Length..].ToLowerInvariant();
                    switch (viewName)
                    {
                        case "desk":
                            captureWindow.NavDesk.IsChecked = true;
                            break;
                        case "sales" or "pos":
                            captureWindow.NavSales.IsChecked = true;
                            break;
                        case "tickets" or "vouchers":
                            captureWindow.NavTickets.IsChecked = true;
                            break;
                        case "members":
                            captureWindow.NavMembers.IsChecked = true;
                            break;
                        case "inventory":
                            captureWindow.NavInventory.IsChecked = true;
                            break;
                        case "peripherals":
                            captureWindow.NavPeripherals.IsChecked = true;
                            break;
                        case "reports":
                            captureWindow.NavReports.IsChecked = true;
                            break;
                        case "alerts":
                            captureWindow.NavAlerts.IsChecked = true;
                            break;
                        case "screen":
                            captureWindow.ViewScreenGrid.IsChecked = true;
                            break;
                        case "perf" or "telemetry":
                            captureWindow.ViewTelemetryGrid.IsChecked = true;
                            break;
                        case "settings":
                            captureWindow.NavSettings.IsChecked = true;
                            break;
                    }
                    captureWindow.UpdateLayout();
                    await Task.Delay(1000);
                }

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(1440, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(captureWindow);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = File.Create(targetPath);
                enc.Save(fs);
                Console.WriteLine($"CAPTURE_SUCCESS: {targetPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CAPTURE_ERROR: {ex}");
            }
            Shutdown(0);
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
        window.Closed += (_, _) => Shutdown(0);
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

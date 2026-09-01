using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Server.App.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ZixCafe.Server.App;

public static class ServerHostFactory
{
    public static WebApplication Build(int port, string connectionString)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders();

        builder.Services.AddDbContextFactory<ZixCafeDbContext>(o =>
        {
            o.UseSqlite(connectionString);
            if (Environment.GetEnvironmentVariable("ZIX_LOG_SQL") == "1")
            {
                o.LogTo(m => Console.WriteLine(m), LogLevel.Information);
            }
        });

        builder.Services.AddSignalR();

        builder.Services.AddSingleton<TerminalRegistry>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<DeskService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RackBroadcaster>());
        builder.Services.AddSingleton<RackBroadcaster>();
        builder.Services.AddHostedService<SessionMonitor>();

        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();

        app.UseCors();
        app.MapHub<TerminalHub>("/hubs/terminal");
        app.MapHub<DashboardHub>("/hubs/dashboard");
        app.MapGet("/health", () => Results.Ok(new { status = "ok", server = "ZixCafe Pro", utc = DateTime.UtcNow }));

        return app;
    }
}

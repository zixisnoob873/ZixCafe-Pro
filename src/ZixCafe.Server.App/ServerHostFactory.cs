using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZixCafe.Infrastructure;
using ZixCafe.Server.App.Hubs;
using ZixCafe.Server.App.Services;

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

        // Core singletons
        builder.Services.AddSingleton<TerminalRegistry>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<DeskService>();

        // Domain & Ops Services
        builder.Services.AddSingleton<AuthAndCashierService>();
        builder.Services.AddSingleton<VenueSettingsService>();
        builder.Services.AddSingleton<TariffService>();
        builder.Services.AddSingleton<AlertsCenterService>();
        builder.Services.AddSingleton<SalesAndPosService>();
        builder.Services.AddSingleton<TicketService>();
        builder.Services.AddSingleton<MemberManagementService>();
        builder.Services.AddSingleton<InventoryService>();
        builder.Services.AddSingleton<PeripheralMeteringService>();
        builder.Services.AddSingleton<ReportsAndAuditService>();
        builder.Services.AddSingleton<RemoteOpsService>();
        builder.Services.AddSingleton<MaintenanceAndReservationService>();
        builder.Services.AddSingleton<ChatHistoryService>();
        builder.Services.AddSingleton<DataCareAndBackupService>();
        builder.Services.AddSingleton<HardwareIntegrityService>();
        builder.Services.AddSingleton<LicenseService>();

        // Background services
        builder.Services.AddSingleton<RackBroadcaster>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RackBroadcaster>());
        builder.Services.AddHostedService<SessionMonitor>();

        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();

        app.UseCors();
        app.MapHub<TerminalHub>("/hubs/terminal");
        app.MapHub<DashboardHub>("/hubs/dashboard");
        app.MapGet("/health", () => Results.Ok(new { status = "ok", server = "ZixCafe Pro", utc = DateTime.UtcNow }));

        // LAN Web Dashboard read-only mirror (Item U in plan)
        app.MapGet("/", () => Results.Content(GetWebDashboardHtml(), "text/html"));
        app.MapGet("/dashboard", () => Results.Content(GetWebDashboardHtml(), "text/html"));

        return app;
    }

    private static string GetWebDashboardHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>ZixCafe Pro — LAN Live Monitor</title>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"></script>
    <style>
        :root {
            --void: #0C0A09;
            --panel: #1C1917;
            --raised: #292524;
            --line: #44403C;
            --ink: #FAFAF9;
            --ghost: #A8A29E;
            --gold: #FBBF24;
            --gold-deep: #D97706;
            --run: #22C55E;
            --warn: #F97316;
            --alert: #EF4444;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            background-color: var(--void);
            color: var(--ink);
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            padding: 24px;
        }
        header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid var(--line);
            padding-bottom: 16px;
            margin-bottom: 24px;
        }
        .brand {
            display: flex;
            align-items: center;
            gap: 12px;
            font-weight: 800;
            letter-spacing: 0.1em;
            color: var(--gold);
            font-size: 1.25rem;
        }
        .stats {
            display: flex;
            gap: 20px;
        }
        .stat-badge {
            background: var(--panel);
            border: 1px solid var(--line);
            padding: 6px 14px;
            border-radius: 4px;
            font-size: 0.85rem;
            color: var(--ghost);
        }
        .stat-badge strong {
            color: var(--ink);
        }
        .grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
            gap: 16px;
        }
        .card {
            background: var(--panel);
            border: 1px solid var(--line);
            border-radius: 6px;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 10px;
            transition: transform 0.15s ease, border-color 0.15s ease;
        }
        .card:hover {
            border-color: var(--gold);
            transform: translateY(-2px);
        }
        .card-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .card-name {
            font-weight: 700;
            font-size: 1.1rem;
            color: var(--ink);
        }
        .status-pill {
            font-size: 0.75rem;
            font-weight: 600;
            padding: 2px 8px;
            border-radius: 3px;
            text-transform: uppercase;
        }
        .status-available { background: rgba(34,197,94,0.15); color: var(--run); border: 1px solid var(--run); }
        .status-inuse { background: rgba(251,191,36,0.15); color: var(--gold); border: 1px solid var(--gold); }
        .status-offline { background: rgba(168,162,158,0.15); color: var(--ghost); border: 1px solid var(--ghost); }
        .status-locked { background: rgba(239,68,68,0.15); color: var(--alert); border: 1px solid var(--alert); }
        .status-maintenance { background: rgba(249,115,22,0.15); color: var(--warn); border: 1px solid var(--warn); }
        .card-body {
            display: flex;
            flex-direction: column;
            gap: 4px;
            font-size: 0.85rem;
            color: var(--ghost);
        }
        .timer {
            font-size: 1.3rem;
            font-weight: 700;
            font-family: monospace;
            color: var(--gold);
        }
        .charge {
            font-family: monospace;
            color: var(--ink);
        }
    </style>
</head>
<body>
    <header>
        <div class="brand">
            <svg width="24" height="24" viewBox="0 0 40 40" fill="none">
                <polygon points="20,2 34,10 34,26 20,34 6,26 6,10" stroke="#FBBF24" stroke-width="3" fill="#1C1917" />
                <polygon points="23,7 12,21 18,21 15,29 26,16 20,16" fill="#FBBF24" />
            </svg>
            <span>ZIXCAFE PRO &bull; LAN MONITOR</span>
        </div>
        <div class="stats">
            <div class="stat-badge">Active Sessions: <strong id="statActive">0</strong></div>
            <div class="stat-badge">Available: <strong id="statAvail">0</strong></div>
            <div class="stat-badge">Offline: <strong id="statOffline">0</strong></div>
        </div>
    </header>

    <div class="grid" id="terminalGrid"></div>

    <script>
        const terminals = new Map();
        const grid = document.getElementById('terminalGrid');

        function render() {
            grid.innerHTML = '';
            let active = 0, avail = 0, offline = 0;

            const sorted = Array.from(terminals.values()).sort((a, b) => a.name.localeCompare(b.name, undefined, {numeric: true}));

            for (const t of sorted) {
                if (t.status === 2) active++;
                else if (t.status === 1) avail++;
                else offline++;

                const card = document.createElement('div');
                card.className = 'card';

                const statusNames = ['offline', 'available', 'inuse', 'locked', 'reserved', 'maintenance'];
                const statusName = statusNames[t.status] || 'offline';

                let timeDisplay = '--:--:--';
                if (t.minutesRemaining !== null && t.minutesRemaining !== undefined) {
                    const hrs = Math.floor(t.minutesRemaining / 60);
                    const mins = t.minutesRemaining % 60;
                    timeDisplay = `${String(hrs).padStart(2, '0')}:${String(mins).padStart(2, '0')}:00`;
                } else if (t.status === 2) {
                    const hrs = Math.floor(t.minutesElapsed / 60);
                    const mins = t.minutesElapsed % 60;
                    timeDisplay = `${String(hrs).padStart(2, '0')}:${String(mins).padStart(2, '0')}:00`;
                }

                card.innerHTML = `
                    <div class="card-header">
                        <span class="card-name">${t.name}</span>
                        <span class="status-pill status-${statusName}">${statusName}</span>
                    </div>
                    <div class="card-body">
                        <div>Zone: <strong>${t.zoneName}</strong></div>
                        ${t.status === 2 ? `<div class="timer">${timeDisplay}</div><div class="charge">Charge: $${(t.currentAmount || 0).toFixed(2)}</div>` : ''}
                        ${t.cpuTemp ? `<div>CPU: ${t.cpuTemp}% | RAM: ${t.ramPercent || 0}%</div>` : ''}
                    </div>
                `;
                grid.appendChild(card);
            }

            document.getElementById('statActive').innerText = active;
            document.getElementById('statAvail').innerText = avail;
            document.getElementById('statOffline').innerText = offline;
        }

        const connection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/dashboard')
            .withAutomaticReconnect()
            .build();

        connection.on('TerminalStateChanged', (state) => {
            terminals.set(state.terminalId, state);
            render();
        });

        connection.start().then(() => {
            connection.invoke('SubscribeAsync');
        }).catch(console.error);
    </script>
</body>
</html>
""";
}

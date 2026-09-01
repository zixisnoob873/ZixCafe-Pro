using ZixCafe.Server.App.Hubs;
using ZixCafe.Shared.Contracts;
using ZixCafe.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace ZixCafe.Server.App.Services;

/// <summary>
/// Fans terminal state changes out to every connected operator dashboard,
/// decoupling hub handlers from the dashboard's connection set.
/// </summary>
public class RackBroadcaster : BackgroundService
{
    private readonly TerminalRegistry _registry;
    private readonly IHubContext<DashboardHub, IDashboardClient> _dashboard;

    public RackBroadcaster(TerminalRegistry registry, IHubContext<DashboardHub, IDashboardClient> dashboard)
    {
        _registry = registry;
        _dashboard = dashboard;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _registry.StateChanged += state =>
        {
            _ = Task.Run(() => _dashboard.Clients.Group("dashboard").TerminalStateChanged(state), stoppingToken);
        };
        return Task.CompletedTask;
    }
}

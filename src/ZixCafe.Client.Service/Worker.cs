namespace ZixCafe.Client.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ZixCafe watchdog started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnsureAgentRunning();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchdog check failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private void EnsureAgentRunning()
    {
        var agents = System.Diagnostics.Process.GetProcessesByName("ZixCafe.Client.Agent");
        if (agents.Length == 0)
        {
            var exePath = AppContext.BaseDirectory;
            var agentPath = Path.Combine(
                Directory.GetParent(exePath)?.FullName ?? exePath,
                "ZixCafe.Client.Agent", "ZixCafe.Client.Agent.exe");
            if (File.Exists(agentPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = agentPath,
                    UseShellExecute = true
                });
                _logger.LogInformation("Agent was down; relaunched from {Path}.", agentPath);
            }
            else
            {
                _logger.LogWarning("Agent executable not found at {Path}.", agentPath);
            }
        }
        else
        {
            foreach (var p in agents)
            {
                p.Dispose();
            }
        }
    }
}

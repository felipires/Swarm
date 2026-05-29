using Swarm.Cluster.Data;

namespace Swarm.Cluster.Services;

/// <summary>
/// Background service that periodically checks for offline nodes
/// </summary>
public class HeartbeatBackgroundService : BackgroundService
{
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _intervalSeconds;

    public HeartbeatBackgroundService(ILogger<HeartbeatBackgroundService> logger, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _intervalSeconds = configuration.GetValue<int>("Heartbeat:CheckIntervalSeconds", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat background service started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckOfflineNodesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking for offline nodes");
                }
            }
        }
        finally
        {
            _logger.LogInformation("Heartbeat background service stopped");
        }
    }

    private async Task CheckOfflineNodesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var nodeService = scope.ServiceProvider.GetRequiredService<NodeService>();

        _logger.LogDebug("Running offline node detection");
        await nodeService.MarkOfflineNodesAsync();
    }
}

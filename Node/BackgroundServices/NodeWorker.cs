using Swarm.Node.Data;
using Swarm.Node.Services;

namespace Swarm.Node.BackgroundServices;

public class NodeWorker(
    ILogger<NodeWorker> logger,
    IServiceProvider serviceProvider,
    BackgroundMaestro backgroundMaestro,
    IConfiguration configuration) : BackgroundService
{
    private readonly ILogger<NodeWorker> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly BackgroundMaestro _backgroundMaestro = backgroundMaestro;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(
        configuration.GetValue<int>("Heartbeat:IntervalSeconds", 5));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Node worker starting, waiting for startup signal");
        await _backgroundMaestro.WaitAsync();

        var heartBeatService = _serviceProvider.GetRequiredService<HeartBeatService>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Node worker starting main loop");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await heartBeatService.SendHeartBeatAsync();
                    await Task.Delay(_heartbeatInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Node worker encountered an error; restarting in 10 seconds");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogDebug("Node worker stopped");
    }


}

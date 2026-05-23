using Swarm.Node.Data;
using Swarm.Node.Services;

namespace Swarm.Node.BackgroundServices;

public class NodeWorker(
    ILogger<NodeWorker> logger, 
    IServiceProvider serviceProvider, 
    BackgroundMaestro backgroundMaestro) : BackgroundService
{
    private readonly ILogger<NodeWorker> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly BackgroundMaestro _backgroundMaestro = backgroundMaestro;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Node worker starting, waiting for startup signal");
        await _backgroundMaestro.WaitAsync();

        ExecutionContext:        

        try
        {            
            _logger.LogDebug("Node worker starting main loop");

            var heartBeatService = _serviceProvider.GetRequiredService<HeartBeatService>();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await heartBeatService.SendHeartBeatAsync();
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in main event loop");
                    throw;
                }
            }

            _logger.LogDebug("Node worker stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node worker encountered an error during execution");
            _logger.LogDebug("Node worker restarting after error in 10 seconds");
            await Task.Delay(10000, stoppingToken);
            goto ExecutionContext;
        }
    }


}

using System;

namespace Swarm.Node.BackgroundServices;

/// <summary>
/// Class that orchestrate the execution order of background services
/// </summary>
public class BackgroundMaestro(ILogger<BackgroundMaestro> logger)
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger<BackgroundMaestro> _logger = logger;
    private readonly object _lock = new(); 
    public void Release()
    {
        lock (_lock)
        {
            if (!_tcs.Task.IsCompleted)
            {
                _logger.LogDebug("Releasing background services to start execution");
                _tcs.TrySetResult();
            }
        }
    }
    public Task WaitAsync()
    {
        _logger.LogDebug("Background services waiting for release signal");
        return _tcs.Task;
    }

}

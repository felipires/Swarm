using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Polls for <see cref="TaskInstance"/> rows whose <c>Status = Pending</c>
/// and <c>RetryAfter</c> has elapsed, and re-emits each via
/// <see cref="TaskDispatchService.RedispatchAsync"/> (roadmap P1-2). The
/// retry decision (whether a failure becomes Failed-terminal or scheduled
/// for retry) lives in <see cref="TaskResultConsumerService"/> — this
/// service is the pure delivery side.
/// </summary>
public class RetrySchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetrySchedulerService> _logger;
    private readonly TimeSpan _pollInterval;
    private const int BatchSize = 100;

    public RetrySchedulerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RetrySchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Retry:PollIntervalSeconds", 10));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retry scheduler started — polling every {Interval}", _pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var picked = await SweepAsync(stoppingToken);
                if (picked == 0)
                    await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry sweep failed; sleeping before next attempt");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Retry scheduler stopping");
    }

    /// <summary>
    /// Single sweep — picks up to <see cref="BatchSize"/> due retries and
    /// redispatches each. Returns the number picked so the caller can
    /// short-circuit the sleep when there's still work.
    /// </summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();
        var dispatch = scope.ServiceProvider.GetRequiredService<TaskDispatchService>();

        var now = DateTime.UtcNow;
        var due = await db.TaskInstances
            .Where(i => i.Status == TaskInstance.TaskInstanceStatus.Pending
                        && i.RetryAfter != null
                        && i.RetryAfter <= now)
            .OrderBy(i => i.RetryAfter)
            .Take(BatchSize)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in due)
        {
            try
            {
                await dispatch.RedispatchAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to redispatch instance {InstanceId}; will retry on next sweep", id);
            }
        }

        return due.Count;
    }
}

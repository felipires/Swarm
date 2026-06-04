using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Single-process implementation of <see cref="IStepAdvancer"/> using an
/// unbounded <see cref="Channel{T}"/>. Hosted as a singleton background
/// service: <see cref="NotifyAsync"/> writes a work item, the long-running
/// processing loop drains the channel and advances each affected pipeline
/// run under a per-run lock so concurrent step completions for the same
/// run can't race.
/// </summary>
public class InProcessStepAdvancer : BackgroundService, IStepAdvancer
{
    private readonly Channel<Guid> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InProcessStepAdvancer> _logger;

    /// <summary>Per-run lock — at most one advancement at a time per pipeline run.</summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _runLocks = new();

    public InProcessStepAdvancer(IServiceScopeFactory scopeFactory, ILogger<InProcessStepAdvancer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ValueTask NotifyAsync(Guid completedTaskInstanceId, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(completedTaskInstanceId, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Step advancer started");

        try
        {
            await foreach (var taskInstanceId in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(taskInstanceId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Step advancer failed processing instance {InstanceId}; pipeline state may need manual recovery",
                        taskInstanceId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }

        _logger.LogInformation("Step advancer stopping");
    }

    private async Task ProcessAsync(Guid taskInstanceId, CancellationToken cancellationToken)
    {
        // Phase 1: look up the step instance under a scoped DbContext.
        Guid pipelineRunId;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();
            var stepInstance = await db.PipelineStepInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TaskInstanceId == taskInstanceId, cancellationToken);

            if (stepInstance is null)
            {
                // Not a pipeline-owned TaskInstance — nothing to advance.
                return;
            }

            pipelineRunId = stepInstance.PipelineRunId;
        }

        // Phase 2: process under a per-run lock so concurrent notifications
        // for the same run serialize. A second scoped DbContext keeps the
        // read+write atomic from EF's perspective.
        var gate = _runLocks.GetOrAdd(pipelineRunId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<PipelineRunExecutor>();
            await executor.AdvanceAsync(pipelineRunId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}

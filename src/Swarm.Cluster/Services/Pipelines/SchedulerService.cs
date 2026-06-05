using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Roadmap P1-3 cron scheduler. One <see cref="BackgroundService"/> instance
/// sweeps the <see cref="Schedule"/> table every
/// <c>Scheduling:PollIntervalSeconds</c> (default 10) and fires due rows
/// through <see cref="PipelineService.StartRunAsync"/>.
///
/// Single-Cluster (D2): no leader election or row-level lock — there's only
/// one writer. If/when HA arrives, the sweep query gets
/// <c>FOR UPDATE SKIP LOCKED</c> and the lease becomes part of D2's
/// extraction.
///
/// Failure semantics: NextFireAt + LastFiredAt are only persisted *after*
/// StartRunAsync returns successfully. If the run trigger throws, the row
/// stays due and the next sweep retries — better to fire late than to lose
/// a tick.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchedulerService> _logger;
    private readonly TimeSpan _pollInterval;
    private const int BatchSize = 100;

    public SchedulerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Scheduling:PollIntervalSeconds", 10));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pipeline scheduler started — polling every {Interval}", _pollInterval);

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
                _logger.LogError(ex, "Scheduler sweep failed; sleeping before next attempt");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Pipeline scheduler stopping");
    }

    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();
        var pipelines = scope.ServiceProvider.GetRequiredService<PipelineService>();

        var now = DateTime.UtcNow;
        var due = await db.Schedules
            .Where(s => s.Enabled && s.NextFireAt != null && s.NextFireAt <= now)
            .OrderBy(s => s.NextFireAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var schedule in due)
        {
            try
            {
                await FireAsync(db, pipelines, schedule, now, cancellationToken);
            }
            catch (Exception ex)
            {
                // Keep NextFireAt as-is so the next sweep retries. Log and
                // continue with the next due row — one bad pipeline must not
                // halt the rest of the fleet.
                _logger.LogError(ex,
                    "Failed to fire schedule {ScheduleId} for pipeline {PipelineId}; will retry on next sweep",
                    schedule.Id, schedule.PipelineId);
            }
        }

        return due.Count;
    }

    private async Task FireAsync(
        ClusterDbContext db,
        PipelineService pipelines,
        Schedule schedule,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var cron = CronScheduleEvaluator.Parse(schedule.CronExpression);
        var tz = CronScheduleEvaluator.ResolveZone(schedule.TimeZoneId);

        await pipelines.StartRunAsync(
            schedule.PipelineId,
            schedule.RuntimeParamsJson,
            triggerReason: $"schedule:{schedule.Id}",
            cancellationToken);

        schedule.LastFiredAt = now;
        schedule.NextFireAt = CronScheduleEvaluator.Next(cron, now, tz);
        schedule.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Schedule {ScheduleId} fired pipeline {PipelineId}; next fire at {NextFireAt:O}",
            schedule.Id, schedule.PipelineId, schedule.NextFireAt);
    }
}

using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Operator-facing CRUD for <see cref="Schedule"/> rows (roadmap P1-3).
/// All cron + timezone validation goes through
/// <see cref="CronScheduleEvaluator"/> so the API surfaces typed errors
/// before persistence. The sweep loop is owned by
/// <see cref="SchedulerService"/>.
/// </summary>
public class ScheduleService
{
    private readonly ClusterDbContext _db;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(ClusterDbContext db, ILogger<ScheduleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Schedule> CreateAsync(
        Guid pipelineId,
        string cronExpression,
        string timeZoneId,
        bool enabled,
        string? runtimeParamsJson,
        CancellationToken cancellationToken)
    {
        var pipelineExists = await _db.Pipelines.AnyAsync(p => p.Id == pipelineId, cancellationToken);
        if (!pipelineExists)
            throw new InvalidOperationException($"Pipeline {pipelineId} not found");

        var cron = CronScheduleEvaluator.Parse(cronExpression);
        var tz = CronScheduleEvaluator.ResolveZone(timeZoneId);

        var now = DateTime.UtcNow;
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            CronExpression = cronExpression,
            TimeZoneId = timeZoneId,
            Enabled = enabled,
            NextFireAt = enabled ? CronScheduleEvaluator.Next(cron, now, tz) : null,
            RuntimeParamsJson = runtimeParamsJson,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Created schedule {ScheduleId} for pipeline {PipelineId} ('{Cron}' {Tz}); next fire {Next:O}",
            schedule.Id, pipelineId, cronExpression, timeZoneId, schedule.NextFireAt);
        return schedule;
    }

    public async Task<Schedule?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<List<Schedule>> ListForPipelineAsync(Guid pipelineId, CancellationToken cancellationToken)
        => await _db.Schedules
            .Where(s => s.PipelineId == pipelineId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<Schedule> UpdateAsync(
        Guid id,
        string? cronExpression,
        string? timeZoneId,
        bool? enabled,
        string? runtimeParamsJson,
        CancellationToken cancellationToken)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {id} not found");

        var cronChanged = cronExpression is not null && cronExpression != schedule.CronExpression;
        var tzChanged = timeZoneId is not null && timeZoneId != schedule.TimeZoneId;
        var enableChanged = enabled is not null && enabled != schedule.Enabled;

        if (cronExpression is not null) schedule.CronExpression = cronExpression;
        if (timeZoneId is not null) schedule.TimeZoneId = timeZoneId;
        if (enabled is not null) schedule.Enabled = enabled.Value;
        if (runtimeParamsJson is not null) schedule.RuntimeParamsJson = runtimeParamsJson;

        if (cronChanged || tzChanged || enableChanged)
        {
            if (schedule.Enabled)
            {
                var cron = CronScheduleEvaluator.Parse(schedule.CronExpression);
                var tz = CronScheduleEvaluator.ResolveZone(schedule.TimeZoneId);
                schedule.NextFireAt = CronScheduleEvaluator.Next(cron, DateTime.UtcNow, tz);
            }
            else
            {
                schedule.NextFireAt = null;
            }
        }

        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return schedule;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {id} not found");
        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

namespace Swarm.Cluster.Models;

/// <summary>
/// A cron-driven trigger for a <see cref="Pipeline"/> (roadmap P1-3).
/// The <see cref="Services.Pipelines.SchedulerService"/> wakes every few
/// seconds, finds schedules whose <see cref="NextFireAt"/> is due, starts
/// a pipeline run, then advances <see cref="NextFireAt"/> to the next
/// cron occurrence.
///
/// All time math runs in <see cref="TimeZoneId"/> so daylight-saving
/// transitions land at the operator-intended wall-clock time; the
/// scheduler stores the resulting UTC instant.
/// </summary>
public class Schedule
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }

    /// <summary>
    /// Cron expression. Five-field UNIX syntax by default; six-field
    /// (with seconds) is also accepted by <c>Cronos</c> if the operator
    /// supplies it. Validated at creation time.
    /// </summary>
    public string CronExpression { get; set; } = null!;

    /// <summary>IANA TZ name (e.g. <c>Europe/Berlin</c>) or <c>UTC</c>.</summary>
    public string TimeZoneId { get; set; } = "UTC";

    public bool Enabled { get; set; } = true;

    /// <summary>UTC instant of the most recent successful trigger.</summary>
    public DateTime? LastFiredAt { get; set; }

    /// <summary>
    /// UTC instant when the next run should fire. Computed at creation /
    /// update time and recomputed after each successful trigger. NULL
    /// when the cron has no more occurrences (rare — e.g. specific dates
    /// in the past).
    /// </summary>
    public DateTime? NextFireAt { get; set; }

    /// <summary>JSON-encoded per-run params forwarded to the pipeline.</summary>
    public string? RuntimeParamsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

using Cronos;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Thin wrapper around <see cref="Cronos.CronExpression"/> that:
/// 1. auto-detects 5-field vs 6-field (with seconds) syntax by counting
///    whitespace-separated tokens;
/// 2. resolves the time zone, returning a typed
///    <see cref="CronScheduleException"/> for any parse or zone error so
///    callers (API + scheduler) get a single failure shape.
///
/// Used by both <see cref="SchedulerService"/> (each sweep) and the
/// schedule-creation API (validation + initial <c>NextFireAt</c>).
/// </summary>
public static class CronScheduleEvaluator
{
    public static CronExpression Parse(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new CronScheduleException("CRON_EMPTY", "Cron expression is required");

        var fieldCount = expression.Split(
            new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        var format = fieldCount switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => throw new CronScheduleException("CRON_INVALID_FORMAT",
                $"Cron expression must have 5 or 6 fields, got {fieldCount}"),
        };

        try
        {
            return CronExpression.Parse(expression, format);
        }
        catch (CronFormatException ex)
        {
            throw new CronScheduleException("CRON_INVALID", ex.Message, ex);
        }
    }

    public static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new CronScheduleException("TIMEZONE_UNKNOWN",
                $"Unknown time zone '{timeZoneId}'", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new CronScheduleException("TIMEZONE_INVALID",
                $"Time zone '{timeZoneId}' is invalid", ex);
        }
    }

    /// <summary>
    /// Computes the next UTC occurrence strictly after <paramref name="fromUtc"/>.
    /// Returns null when the cron has no more occurrences (e.g. specific
    /// dates fully in the past).
    /// </summary>
    public static DateTime? Next(CronExpression cron, DateTime fromUtc, TimeZoneInfo tz)
    {
        if (fromUtc.Kind != DateTimeKind.Utc)
            fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        return cron.GetNextOccurrence(fromUtc, tz, inclusive: false);
    }
}

public sealed class CronScheduleException : Exception
{
    public string Code { get; }
    public CronScheduleException(string code, string message, Exception? inner = null)
        : base(message, inner) { Code = code; }
}

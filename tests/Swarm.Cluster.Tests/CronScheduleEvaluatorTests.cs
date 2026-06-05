using FluentAssertions;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

public class CronScheduleEvaluatorTests
{
    [Fact]
    public void Parse_FiveField_AcceptsStandardCron()
    {
        var cron = CronScheduleEvaluator.Parse("0 9 * * 1-5");
        cron.Should().NotBeNull();
    }

    [Fact]
    public void Parse_SixField_AcceptsSecondsCron()
    {
        var cron = CronScheduleEvaluator.Parse("0 0 9 * * 1-5");
        cron.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        var act = () => CronScheduleEvaluator.Parse("");
        act.Should().Throw<CronScheduleException>().Where(e => e.Code == "CRON_EMPTY");
    }

    [Fact]
    public void Parse_WrongFieldCount_Throws()
    {
        var act = () => CronScheduleEvaluator.Parse("0 9 * *");
        act.Should().Throw<CronScheduleException>().Where(e => e.Code == "CRON_INVALID_FORMAT");
    }

    [Fact]
    public void Parse_BadExpression_Throws()
    {
        var act = () => CronScheduleEvaluator.Parse("nope nope nope nope nope");
        act.Should().Throw<CronScheduleException>().Where(e => e.Code == "CRON_INVALID");
    }

    [Fact]
    public void ResolveZone_KnownTz_Returns()
    {
        var tz = CronScheduleEvaluator.ResolveZone("UTC");
        tz.Id.Should().Be("UTC");
    }

    [Fact]
    public void ResolveZone_Unknown_Throws()
    {
        var act = () => CronScheduleEvaluator.ResolveZone("Not/AZone");
        act.Should().Throw<CronScheduleException>().Where(e => e.Code == "TIMEZONE_UNKNOWN");
    }

    [Fact]
    public void Next_ReturnsFutureUtcOccurrence()
    {
        var cron = CronScheduleEvaluator.Parse("0 * * * *"); // top of every hour
        var tz = TimeZoneInfo.Utc;
        var from = new DateTime(2026, 1, 1, 10, 15, 0, DateTimeKind.Utc);
        var next = CronScheduleEvaluator.Next(cron, from, tz);
        next.Should().Be(new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));
    }
}

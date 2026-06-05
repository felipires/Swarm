using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Single-tick sweep tests for <see cref="SchedulerService"/>. The
/// background loop itself is just a `while` + delay around
/// <c>SweepAsync</c>, so verifying the sweep is sufficient.
/// </summary>
public class SchedulerServiceTests
{
    [Fact]
    public async Task SweepAsync_DueSchedule_FiresAndAdvancesNextFireAt()
    {
        await using var harness = await Harness.BuildAsync();
        var (pipelineId, taskDefId) = await harness.SeedPipelineAsync();

        var past = DateTime.UtcNow.AddMinutes(-1);
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            CronExpression = "0 * * * *",
            TimeZoneId = "UTC",
            Enabled = true,
            NextFireAt = past,
            CreatedAt = past,
            UpdatedAt = past,
        };
        harness.Db.Schedules.Add(schedule);
        await harness.Db.SaveChangesAsync();

        var picked = await harness.Sweeper.SweepAsync(default);
        picked.Should().Be(1);

        var fresh = await harness.Db.Schedules.AsNoTracking().SingleAsync(s => s.Id == schedule.Id);
        fresh.LastFiredAt.Should().NotBeNull();
        fresh.NextFireAt.Should().BeAfter(DateTime.UtcNow);

        var runs = await harness.Db.PipelineRuns.Where(r => r.PipelineId == pipelineId).ToListAsync();
        runs.Should().HaveCount(1);
        runs[0].TriggerReason.Should().Be($"schedule:{schedule.Id}");
    }

    [Fact]
    public async Task SweepAsync_DisabledSchedule_NotPicked()
    {
        await using var harness = await Harness.BuildAsync();
        var (pipelineId, _) = await harness.SeedPipelineAsync();

        harness.Db.Schedules.Add(new Schedule
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            CronExpression = "0 * * * *",
            TimeZoneId = "UTC",
            Enabled = false,
            NextFireAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await harness.Db.SaveChangesAsync();

        var picked = await harness.Sweeper.SweepAsync(default);
        picked.Should().Be(0);
    }

    [Fact]
    public async Task SweepAsync_FutureSchedule_NotPicked()
    {
        await using var harness = await Harness.BuildAsync();
        var (pipelineId, _) = await harness.SeedPipelineAsync();

        harness.Db.Schedules.Add(new Schedule
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            CronExpression = "0 * * * *",
            TimeZoneId = "UTC",
            Enabled = true,
            NextFireAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        var picked = await harness.Sweeper.SweepAsync(default);
        picked.Should().Be(0);
    }

    [Fact]
    public async Task ScheduleService_Create_RejectsInvalidCron()
    {
        await using var harness = await Harness.BuildAsync();
        var (pipelineId, _) = await harness.SeedPipelineAsync();

        var act = () => harness.Schedules.CreateAsync(
            pipelineId, "not a cron", "UTC", true, null, default);

        await act.Should().ThrowAsync<CronScheduleException>();
    }

    [Fact]
    public async Task ScheduleService_Create_RejectsUnknownPipeline()
    {
        await using var harness = await Harness.BuildAsync();
        var act = () => harness.Schedules.CreateAsync(
            Guid.NewGuid(), "0 * * * *", "UTC", true, null, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ScheduleService_UpdateDisable_NullsNextFireAt()
    {
        await using var harness = await Harness.BuildAsync();
        var (pipelineId, _) = await harness.SeedPipelineAsync();

        var schedule = await harness.Schedules.CreateAsync(
            pipelineId, "0 * * * *", "UTC", true, null, default);
        schedule.NextFireAt.Should().NotBeNull();

        var updated = await harness.Schedules.UpdateAsync(
            schedule.Id, cronExpression: null, timeZoneId: null,
            enabled: false, runtimeParamsJson: null, default);
        updated.NextFireAt.Should().BeNull();
    }

    // ---- harness ----------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        public ClusterDbContext Db { get; }
        public ScheduleService Schedules { get; }
        public SchedulerService Sweeper { get; }

        private Harness(ClusterDbContext db, ScheduleService schedules, SchedulerService sweeper)
        {
            Db = db; Schedules = schedules; Sweeper = sweeper;
        }

        public static Task<Harness> BuildAsync()
        {
            var dbName = $"sched-{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ClusterDbContext>(o => o
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
            services.AddScoped<TaskDispatchService>(sp =>
                new TaskDispatchService(sp.GetRequiredService<ClusterDbContext>(), NullLogger<TaskDispatchService>.Instance));
            services.AddScoped<PipelineRunExecutor>();
            services.AddScoped<PipelineService>();
            services.AddScoped<ScheduleService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scheduling:PollIntervalSeconds"] = "10",
            }).Build();
            services.AddSingleton<IConfiguration>(config);

            var sp = services.BuildServiceProvider();
            var db = sp.GetRequiredService<ClusterDbContext>();
            var schedules = sp.GetRequiredService<ScheduleService>();
            var sweeper = new SchedulerService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                config,
                NullLogger<SchedulerService>.Instance);
            return Task.FromResult(new Harness(db, schedules, sweeper));
        }

        public async Task<(Guid PipelineId, Guid TaskDefinitionId)> SeedPipelineAsync()
        {
            var node = new Node { Id = Guid.NewGuid(), Name = "n", Status = Node.NodeStatus.Online };
            Db.Nodes.Add(node);

            var taskDef = new TaskDefinition
            {
                Id = Guid.NewGuid(),
                Name = "t",
                TaskType = "default@1",
                ConfigJson = "{}",
                DefaultStrategy = DispatchStrategy.SpecificNode,
                Version = 1,
            };
            Db.TaskDefinitions.Add(taskDef);
            await Db.SaveChangesAsync();

            // Build through PipelineService so step IDs, snapshot json etc.
            // exercise the real path.
            var pipelineSvc = new PipelineService(Db,
                new PipelineRunExecutor(Db,
                    new TaskDispatchService(Db, NullLogger<TaskDispatchService>.Instance),
                    NullLogger<PipelineRunExecutor>.Instance),
                NullLogger<PipelineService>.Instance);
            var pipeline = await pipelineSvc.CreateAsync("p", null,
                new List<PipelineService.StepDefinition>
                {
                    new("only", taskDef.Id, Array.Empty<string>(),
                        StrategyOverride: DispatchStrategy.SpecificNode,
                        TargetNodeId: node.Id),
                }, default);
            return (pipeline.Id, taskDef.Id);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}

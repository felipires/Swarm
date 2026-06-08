using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Rerun-failed-steps: a failed run is resumed as a new run that reuses
/// completed steps (carrying their results) and re-attempts the failed frontier.
/// </summary>
public class RetryFailedRunTests
{
    [Fact]
    public async Task RetryFailed_ReusesCompletedSteps_AndSeedsANewRun()
    {
        using var db = BuildDb();
        var svc = Build(db);
        var taskId = await SeedTaskAsync(db);

        var pipeline = await svc.CreateAsync("p", null, new List<PipelineService.StepDefinition>
        {
            new("extract", taskId, DependsOnByName: Array.Empty<string>()),
            new("load", taskId, DependsOnByName: new[] { "extract" }),
        }, CancellationToken.None);

        var extract = pipeline.Steps.First(s => s.Name == "extract");
        var load = pipeline.Steps.First(s => s.Name == "load");

        // Hand-build a failed source run: extract Completed (with a result), load Failed.
        var source = SeedFailedRun(db, pipeline,
            completed: (extract.Id, "{\"id\":42}"),
            failed: load.Id);

        var retried = await svc.RetryFailedAsync(source.Id, CancellationToken.None);

        retried.Id.Should().NotBe(source.Id);
        retried.PipelineVersion.Should().Be(source.PipelineVersion);
        retried.StepsSnapshotJson.Should().Be(source.StepsSnapshotJson);

        var newSteps = await db.PipelineStepInstances
            .Where(s => s.PipelineRunId == retried.Id)
            .ToListAsync();
        newSteps.Should().HaveCount(2);

        // The completed step is reused as-is — not reset, not re-executed.
        var newExtract = newSteps.Single(s => s.PipelineStepId == extract.Id);
        newExtract.Status.Should().Be(PipelineStepInstanceStatus.Completed);
        newExtract.ResultJson.Should().Be("{\"id\":42}");

        // The failed step was re-attempted (no longer Waiting/Completed-from-source).
        var newLoad = newSteps.Single(s => s.PipelineStepId == load.Id);
        newLoad.ResultJson.Should().BeNull();
    }

    [Fact]
    public async Task RetryFailed_OnNonFailedRun_Throws()
    {
        using var db = BuildDb();
        var svc = Build(db);
        var taskId = await SeedTaskAsync(db);
        var pipeline = await svc.CreateAsync("p", null, new List<PipelineService.StepDefinition>
        {
            new("only", taskId, DependsOnByName: Array.Empty<string>()),
        }, CancellationToken.None);

        var running = SeedFailedRun(db, pipeline, completed: null, failed: null,
            status: PipelineRunStatus.Completed);

        var act = () => svc.RetryFailedAsync(running.Id, CancellationToken.None);
        await act.Should().ThrowAsync<PipelineService.RunNotRetryableException>();
    }

    // ---- harness ----

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"retry-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }

    private static PipelineService Build(ClusterDbContext db)
    {
        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        var executor = new PipelineRunExecutor(db, dispatch, NullLogger<PipelineRunExecutor>.Instance);
        return new PipelineService(db, executor, new EntityVersionService(db), NullLogger<PipelineService>.Instance);
    }

    private static async Task<Guid> SeedTaskAsync(ClusterDbContext db)
    {
        var t = new TaskDefinition
        {
            Id = Guid.NewGuid(), Name = "t", TaskType = "default@1", ConfigJson = "{}",
            DefaultStrategy = DispatchStrategy.AllOnlineNodes, Version = 1,
        };
        db.TaskDefinitions.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private static PipelineRun SeedFailedRun(
        ClusterDbContext db, Pipeline pipeline,
        (Guid stepId, string result)? completed, Guid? failed,
        PipelineRunStatus status = PipelineRunStatus.Failed)
    {
        var snapshots = pipeline.Steps.Select(s => new PipelineRunExecutor.StepSnapshot(
            StepId: s.Id, Name: s.Name, TaskDefinitionId: s.TaskDefinitionId,
            DependsOn: PipelineGraph.DependencyDecoder.Decode(s.DependsOnJson).ToList(),
            Strategy: s.StrategyOverride, TargetNodeId: s.TargetNodeId, TargetTagsJson: s.TargetTagsJson,
            FailurePolicy: s.FailurePolicy)).ToList();

        var run = new PipelineRun
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            PipelineVersion = pipeline.Version,
            StepsSnapshotJson = JsonSerializer.Serialize(snapshots),
            Status = status,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CompletedAt = DateTime.UtcNow.AddMinutes(-4),
        };
        db.PipelineRuns.Add(run);

        foreach (var step in pipeline.Steps)
        {
            var st = PipelineStepInstanceStatus.Completed;
            string? result = null;
            if (completed is { } c && c.stepId == step.Id) { st = PipelineStepInstanceStatus.Completed; result = c.result; }
            else if (failed == step.Id) st = PipelineStepInstanceStatus.Failed;
            else st = PipelineStepInstanceStatus.Completed;

            db.PipelineStepInstances.Add(new PipelineStepInstance
            {
                Id = Guid.NewGuid(),
                PipelineRunId = run.Id,
                PipelineStepId = step.Id,
                Status = st,
                ResultJson = result,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow.AddMinutes(-4),
            });
        }
        db.SaveChanges();
        return run;
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// End-to-end pipeline lifecycle: define a small DAG, start a run, verify
/// the snapshot + step instances are persisted correctly and the root step
/// is dispatched. Uses EF InMemory and bypasses the broker via the
/// no-validator TaskDispatchService overload.
/// </summary>
public class PipelineServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidatesDanglingDependencyByName()
    {
        using var db = BuildDb();
        var (svc, _, _) = await BuildAsync(db);

        var taskDef = await SeedTaskDefinitionAsync(db);

        var act = () => svc.CreateAsync(
            "broken",
            description: null,
            steps: new List<PipelineService.StepDefinition>
            {
                new("a", taskDef.Id, DependsOnByName: new[] { "ghost" }),
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<PipelineGraphException>()
            .Where(e => e.Code == "DANGLING_DEPENDENCY");
    }

    [Fact]
    public async Task CreateAsync_PersistsStepsWithResolvedDependencyIds()
    {
        using var db = BuildDb();
        var (svc, _, _) = await BuildAsync(db);
        var taskDef = await SeedTaskDefinitionAsync(db);

        var pipeline = await svc.CreateAsync(
            "etl",
            description: "extract → transform → load",
            steps: new List<PipelineService.StepDefinition>
            {
                new("extract", taskDef.Id, DependsOnByName: Array.Empty<string>()),
                new("transform", taskDef.Id, DependsOnByName: new[] { "extract" }),
                new("load", taskDef.Id, DependsOnByName: new[] { "transform" }),
            },
            CancellationToken.None);

        pipeline.Steps.Should().HaveCount(3);
        var byName = pipeline.Steps.ToDictionary(s => s.Name);

        // extract is a root
        PipelineGraph.DependencyDecoder.Decode(byName["extract"].DependsOnJson)
            .Should().BeEmpty();

        // transform depends on extract's resolved id
        PipelineGraph.DependencyDecoder.Decode(byName["transform"].DependsOnJson)
            .Should().BeEquivalentTo(new[] { byName["extract"].Id });

        // load depends on transform's resolved id
        PipelineGraph.DependencyDecoder.Decode(byName["load"].DependsOnJson)
            .Should().BeEquivalentTo(new[] { byName["transform"].Id });
    }

    [Fact]
    public async Task StartRunAsync_PersistsSnapshotAndCreatesWaitingStepInstances()
    {
        using var db = BuildDb();
        var (svc, _, nodeId) = await BuildAsync(db, seedOnlineNode: true);
        var taskDef = await SeedTaskDefinitionAsync(db);

        var pipeline = await svc.CreateAsync(
            "p",
            description: null,
            steps: new List<PipelineService.StepDefinition>
            {
                new("root", taskDef.Id, DependsOnByName: Array.Empty<string>(),
                    StrategyOverride: DispatchStrategy.SpecificNode, TargetNodeId: nodeId),
                new("child", taskDef.Id, DependsOnByName: new[] { "root" },
                    StrategyOverride: DispatchStrategy.SpecificNode, TargetNodeId: nodeId),
            },
            CancellationToken.None);

        var run = await svc.StartRunAsync(pipeline.Id,
            runtimeParamsJson: """{"k":"v"}""",
            triggerReason: "manual",
            CancellationToken.None);

        run.PipelineVersion.Should().Be(pipeline.Version);
        run.StepsSnapshotJson.Should().NotBeNullOrEmpty();
        run.RuntimeParamsJson.Should().Be("""{"k":"v"}""");

        // Per-step instances created.
        var stepInstances = await db.PipelineStepInstances
            .Where(s => s.PipelineRunId == run.Id)
            .ToListAsync();
        stepInstances.Should().HaveCount(2);

        // After DispatchRootsAsync runs synchronously inside StartRunAsync,
        // the root step should be Dispatched and the child still Waiting.
        var byStepId = stepInstances.ToDictionary(s => s.PipelineStepId);
        var rootStep = pipeline.Steps.Single(s => s.Name == "root");
        var childStep = pipeline.Steps.Single(s => s.Name == "child");

        byStepId[rootStep.Id].Status.Should().Be(PipelineStepInstanceStatus.Dispatched);
        byStepId[rootStep.Id].TaskInstanceId.Should().NotBeNull();
        byStepId[childStep.Id].Status.Should().Be(PipelineStepInstanceStatus.Waiting);
        byStepId[childStep.Id].TaskInstanceId.Should().BeNull();
    }

    // ---- harness helpers ----------------------------------------------------

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"pipe-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }

    private static async Task<(PipelineService Service, TaskDispatchService Dispatch, Guid? NodeId)> BuildAsync(
        ClusterDbContext db, bool seedOnlineNode = false)
    {
        // TaskDispatchService without validator — keeps tests focused on the
        // pipeline contract, not on capability eligibility (which has its
        // own tests). Dispatch will write the outbox row regardless.
        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        var executor = new PipelineRunExecutor(db, dispatch, NullLogger<PipelineRunExecutor>.Instance);
        var svc = new PipelineService(db, executor, NullLogger<PipelineService>.Instance);

        Guid? nodeId = null;
        if (seedOnlineNode)
        {
            var node = new Node
            {
                Id = Guid.NewGuid(),
                Name = "n",
                Status = Node.NodeStatus.Online,
            };
            db.Nodes.Add(node);
            await db.SaveChangesAsync();
            nodeId = node.Id;
        }
        return (svc, dispatch, nodeId);
    }

    private static async Task<TaskDefinition> SeedTaskDefinitionAsync(ClusterDbContext db)
    {
        var t = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "t",
            TaskType = "default@1",
            ConfigJson = "{}",
            DefaultStrategy = DispatchStrategy.SpecificNode,
            Version = 1,
        };
        db.TaskDefinitions.Add(t);
        await db.SaveChangesAsync();
        return t;
    }
}

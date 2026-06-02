using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P0-3a hybrid queue topology: which queue the dispatch lands on, and
/// whether <see cref="TaskInstance.NodeId"/> is bound at dispatch time
/// (per-node strategies) or left NULL for later claim (shared strategies).
/// </summary>
public class DispatchStrategyTests
{
    [Fact]
    public async Task SpecificNode_RoutesToPerNodeQueue_AndBindsNodeId()
    {
        using var db = BuildDb();
        var (definition, node) = await SeedAsync(db, taskType: "http@1", online: true);
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(definition.Id, nodeId: node.Id, strategy: DispatchStrategy.SpecificNode);

        instance.NodeId.Should().Be(node.Id);
        var pending = await db.PendingDispatches.SingleAsync();
        pending.QueueName.Should().Be(TaskDispatchService.TaskQueueName(node.Id));
    }

    [Fact]
    public async Task AnyOnlineNode_RoutesToSharedQueue_AndLeavesNodeIdNull()
    {
        using var db = BuildDb();
        var (definition, node) = await SeedAsync(db, taskType: "http@1", online: true);
        await db.NodeCapabilities.AddRangeAsync(new NodeCapability { Id = Guid.NewGuid(), NodeId = node.Id, TaskType = "http@1" });
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(definition.Id, strategy: DispatchStrategy.AnyOnlineNode);

        instance.NodeId.Should().BeNull("shared-queue dispatches stay unbound until a Node publishes a claim");
        var pending = await db.PendingDispatches.SingleAsync();
        pending.QueueName.Should().Be(TaskDispatchService.SharedQueueName("http@1"));
    }

    [Fact]
    public async Task AnyOnlineNode_NoCapableConsumer_Throws()
    {
        using var db = BuildDb();
        var (definition, _) = await SeedAsync(db, taskType: "http@1", online: true);
        // SeedAsync now adds a capability for the seeded Node — strip it so
        // we genuinely have no consumer advertising the TaskType.
        db.NodeCapabilities.RemoveRange(db.NodeCapabilities);
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var act = async () => await service.DispatchAsync(definition.Id, strategy: DispatchStrategy.AnyOnlineNode);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No online Node*advertises*http@1*");
    }

    [Fact]
    public async Task TaggedNodes_DispatchesToSharedTaggedQueue_WithNullNodeId()
    {
        using var db = BuildDb();
        var (definition, _) = await SeedAsync(db, taskType: "http@1", online: true);

        var euNode = new Node
        {
            Id = Guid.NewGuid(),
            Name = "eu-node",
            Status = Node.NodeStatus.Online,
            StaticTagsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "eu" }),
        };
        var usNode = new Node
        {
            Id = Guid.NewGuid(),
            Name = "us-node",
            Status = Node.NodeStatus.Online,
            StaticTagsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "us" }),
        };
        db.Nodes.AddRange(euNode, usNode);
        db.NodeCapabilities.AddRange(
            new NodeCapability { Id = Guid.NewGuid(), NodeId = euNode.Id, TaskType = "http@1", JsonSchema = "{}", RequiredEnvKeysJson = "[]", RequiredParamsJson = "[]", ReportedAt = DateTime.UtcNow },
            new NodeCapability { Id = Guid.NewGuid(), NodeId = usNode.Id, TaskType = "http@1", JsonSchema = "{}", RequiredEnvKeysJson = "[]", RequiredParamsJson = "[]", ReportedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(
            definition.Id,
            strategy: DispatchStrategy.TaggedNodes,
            targetTags: new Dictionary<string, string> { ["region"] = "eu" });

        // New shape: shared queue, NodeId stays null until claim flow binds it.
        instance.NodeId.Should().BeNull(
            "TaggedNodes is now a real shared-queue strategy — the picking Node is bound via the claim flow");

        // PendingDispatch row should target the deterministic tasks.tagged.<hash> queue.
        var pending = await db.PendingDispatches.SingleAsync(p => p.InstanceId == instance.Id);
        var hash = TaggedRouteHash.Compute(new Dictionary<string, string> { ["region"] = "eu" }).Hash;
        pending.QueueName.Should().Be($"tasks.tagged.{hash}");

        // TaggedRoute should be persisted so subsequent heartbeats can advertise it.
        (await db.TaggedRoutes.AnyAsync(r => r.Hash == hash)).Should().BeTrue();
    }

    [Fact]
    public async Task TaggedNodes_NoMatchingNode_Throws()
    {
        using var db = BuildDb();
        var (definition, node) = await SeedAsync(db, taskType: "http@1", online: true);
        // Seeded node already has the http@1 capability; give it tags that
        // don't match the selector below so eligibility fails on tags alone.
        node.StaticTagsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "us" });
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var act = async () => await service.DispatchAsync(
            definition.Id,
            strategy: DispatchStrategy.TaggedNodes,
            targetTags: new Dictionary<string, string> { ["region"] = "eu" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No online Node satisfies tag selector*");
    }

    [Fact]
    public async Task Dispatch_FallsBackToTaskDefinitionDefaultStrategy()
    {
        using var db = BuildDb();
        var (definition, node) = await SeedAsync(db, taskType: "http@1", online: true);
        definition.DefaultStrategy = DispatchStrategy.AnyOnlineNode;
        await db.NodeCapabilities.AddRangeAsync(new NodeCapability { Id = Guid.NewGuid(), NodeId = node.Id, TaskType = "http@1" });
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(definition.Id);

        instance.NodeId.Should().BeNull("the TaskDefinition.DefaultStrategy=AnyOnlineNode should apply when the request omits one");
    }

    private static async Task<(TaskDefinition def, Node node)> SeedAsync(ClusterDbContext db, string taskType, bool online)
    {
        var def = new TaskDefinition { Id = Guid.NewGuid(), Name = "t", TaskType = taskType };
        var node = new Node
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Status = online ? Node.NodeStatus.Online : Node.NodeStatus.Offline,
        };
        db.TaskDefinitions.Add(def);
        db.Nodes.Add(node);
        // P0-3a now requires the eligible Node to advertise the TaskType for
        // shared / tagged dispatch — seed the capability so the existing tests
        // line up with the new query.
        db.NodeCapabilities.Add(new NodeCapability
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            TaskType = taskType,
            JsonSchema = "{}",
            RequiredEnvKeysJson = "[]",
            RequiredParamsJson = "[]",
            ReportedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (def, node);
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"strategy-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

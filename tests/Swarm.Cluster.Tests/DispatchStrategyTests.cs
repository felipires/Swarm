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
        // Deliberately add NO capability for http@1.
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var act = async () => await service.DispatchAsync(definition.Id, strategy: DispatchStrategy.AnyOnlineNode);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No online Node*advertises*http@1*");
    }

    [Fact]
    public async Task TaggedNodes_PicksOneEligibleNode_AndDispatchesAsSpecific()
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
        await db.SaveChangesAsync();
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(
            definition.Id,
            strategy: DispatchStrategy.TaggedNodes,
            targetTags: new Dictionary<string, string> { ["region"] = "eu" });

        instance.NodeId.Should().Be(euNode.Id, "only the eu-tagged Node satisfies the selector");
    }

    [Fact]
    public async Task TaggedNodes_NoMatchingNode_Throws()
    {
        using var db = BuildDb();
        var (definition, node) = await SeedAsync(db, taskType: "http@1", online: true);
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

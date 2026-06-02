using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P2-4: broadcast dispatch writes one TaskInstance + one PendingDispatch per
/// online Node in a single transaction. We can't observe "one transaction"
/// directly with EF InMemory, but we can verify the row shape and counts.
/// </summary>
public class DispatchToAllOnlineTests
{
    [Fact]
    public async Task DispatchToAll_WritesOneInstanceAndOnePendingPerOnlineNode()
    {
        using var db = BuildDb();
        var definition = await SeedDefinitionAsync(db, "http@1");
        var nodeA = await SeedNodeAsync(db, online: true);
        var nodeB = await SeedNodeAsync(db, online: true);
        await SeedNodeAsync(db, online: false); // offline — must be excluded

        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instances = await service.DispatchToAllOnlineAsync(definition.Id);

        instances.Should().HaveCount(2, "two nodes are online");
        instances.Select(i => i.NodeId).Should().BeEquivalentTo(new Guid?[] { nodeA.Id, nodeB.Id });

        var pendings = await db.PendingDispatches.ToListAsync();
        pendings.Should().HaveCount(2);
        pendings.Select(p => p.QueueName).Should().BeEquivalentTo(new[]
        {
            TaskDispatchService.TaskQueueName(nodeA.Id),
            TaskDispatchService.TaskQueueName(nodeB.Id),
        });
        pendings.Select(p => p.InstanceId).Should().BeEquivalentTo(instances.Select(i => i.Id));
    }

    [Fact]
    public async Task DispatchToAll_NoOnlineNodes_Throws()
    {
        using var db = BuildDb();
        var definition = await SeedDefinitionAsync(db, "http@1");
        await SeedNodeAsync(db, online: false);
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var act = async () => await service.DispatchToAllOnlineAsync(definition.Id);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No online nodes*");
    }

    [Fact]
    public async Task DispatchToAll_PropagatesRuntimeParamsIntoEachInstance()
    {
        using var db = BuildDb();
        var definition = await SeedDefinitionAsync(db, "http@1");
        await SeedNodeAsync(db, online: true);
        await SeedNodeAsync(db, online: true);
        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instances = await service.DispatchToAllOnlineAsync(definition.Id, runtimeParamsJson: """{"a":1}""");

        instances.Should().AllSatisfy(i =>
            i.RuntimeParamsJson.Should().Be("""{"a":1}"""));
    }

    private static async Task<TaskDefinition> SeedDefinitionAsync(ClusterDbContext db, string taskType)
    {
        var def = new TaskDefinition { Id = Guid.NewGuid(), Name = "t", TaskType = taskType };
        db.TaskDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def;
    }

    private static async Task<Node> SeedNodeAsync(ClusterDbContext db, bool online)
    {
        var node = new Node
        {
            Id = Guid.NewGuid(),
            Name = $"n-{Guid.NewGuid():N}",
            Status = online ? Node.NodeStatus.Online : Node.NodeStatus.Offline,
        };
        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        return node;
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"dispatch-all-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

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
/// Exercises the P0-4 contract: <see cref="TaskDispatchService.DispatchAsync"/>
/// must insert exactly one <see cref="TaskInstance"/> AND one matching
/// <see cref="PendingDispatch"/> row, with the TaskInstance staying in
/// <c>Pending</c> (the OutboxPublisherService is what transitions it to
/// <c>Dispatched</c> after the real publish succeeds). EF InMemory is used
/// so the tests do not require Postgres.
/// </summary>
public class TaskDispatchOutboxTests
{
    [Fact]
    public async Task DispatchAsync_WritesInstanceAndPendingDispatch_WithSerializedMessage()
    {
        using var db = BuildDb();
        var definition = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "http-sync",
            TaskType = "http@1",
            ConfigJson = """{"url":"https://example.com"}""",
        };
        var node = new Node
        {
            Id = Guid.NewGuid(),
            Name = "node-a",
            Status = Node.NodeStatus.Online,
        };
        db.TaskDefinitions.Add(definition);
        db.Nodes.Add(node);
        await db.SaveChangesAsync();

        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var instance = await service.DispatchAsync(definition.Id, node.Id);

        instance.Status.Should().Be(TaskInstance.TaskInstanceStatus.Pending,
            "the outbox publisher is what transitions Pending → Dispatched");

        var pending = await db.PendingDispatches.SingleAsync();
        pending.InstanceId.Should().Be(instance.Id);
        pending.QueueName.Should().Be(TaskDispatchService.TaskQueueName(node.Id));
        pending.PublishedAt.Should().BeNull();
        pending.Attempts.Should().Be(0);

        var payload = JsonSerializer.Deserialize<TaskMessage>(pending.Payload)!;
        payload.TaskType.Should().Be("http@1");
        payload.InstanceId.Should().Be(instance.Id);
        payload.NodeId.Should().Be(node.Id);
        payload.ConfigJson.Should().Be(definition.ConfigJson);
    }

    [Fact]
    public async Task DispatchAsync_OfflineNode_Throws_AndWritesNothing()
    {
        using var db = BuildDb();
        var definition = new TaskDefinition { Id = Guid.NewGuid(), Name = "x" };
        var node = new Node { Id = Guid.NewGuid(), Name = "n", Status = Node.NodeStatus.Offline };
        db.TaskDefinitions.Add(definition);
        db.Nodes.Add(node);
        await db.SaveChangesAsync();

        var service = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);

        var act = async () => await service.DispatchAsync(definition.Id, node.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.TaskInstances.CountAsync()).Should().Be(0);
        (await db.PendingDispatches.CountAsync()).Should().Be(0);
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"outbox-{Guid.NewGuid()}")
            // Suppress the "InMemory doesn't support transactions" warning so
            // tx.CommitAsync() in DispatchAsync no-ops cleanly.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

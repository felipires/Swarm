using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P1-4: TaskDefinition edits must not affect in-flight TaskInstances.
/// The instance's snapshot fields (TaskType, ConfigJsonSnapshot,
/// TaskDefinitionVersion) capture the state at dispatch time.
/// </summary>
public class TaskInstanceSnapshotTests
{
    [Fact]
    public async Task DispatchAsync_SnapshotsDefinitionAtDispatchTime()
    {
        using var db = BuildDb();
        var node = new Node { Id = Guid.NewGuid(), Name = "n", Status = Node.NodeStatus.Online };
        var def = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "x",
            TaskType = "http@1",
            ConfigJson = """{"url":"https://v1.example"}""",
            Version = 7,
        };
        db.Nodes.Add(node);
        db.TaskDefinitions.Add(def);
        db.NodeCapabilities.Add(new NodeCapability
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            TaskType = "http@1",
            JsonSchema = "{}",
            RequiredEnvKeysJson = "[]",
            RequiredParamsJson = "[]",
            ReportedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        var instance = await dispatch.DispatchAsync(def.Id, node.Id);

        instance.TaskType.Should().Be("http@1");
        instance.ConfigJsonSnapshot.Should().Be("""{"url":"https://v1.example"}""");
        instance.TaskDefinitionVersion.Should().Be(7);
    }

    [Fact]
    public async Task DispatchedInstance_UnaffectedByLaterDefinitionEdits()
    {
        using var db = BuildDb();
        var node = new Node { Id = Guid.NewGuid(), Name = "n", Status = Node.NodeStatus.Online };
        var def = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "x",
            TaskType = "http@1",
            ConfigJson = """{"url":"https://v1.example"}""",
            Version = 1,
        };
        db.Nodes.Add(node);
        db.TaskDefinitions.Add(def);
        db.NodeCapabilities.Add(new NodeCapability
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            TaskType = "http@1",
            JsonSchema = "{}",
            RequiredEnvKeysJson = "[]",
            RequiredParamsJson = "[]",
            ReportedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        var instance = await dispatch.DispatchAsync(def.Id, node.Id);

        // Operator edits the definition after dispatch.
        def.ConfigJson = """{"url":"https://v2.example"}""";
        def.Version = 2;
        await db.SaveChangesAsync();

        // The in-flight instance is unchanged — Node still executes v1.
        var refreshed = await db.TaskInstances.FindAsync(instance.Id);
        refreshed!.ConfigJsonSnapshot.Should().Be("""{"url":"https://v1.example"}""");
        refreshed.TaskDefinitionVersion.Should().Be(1);
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"snapshot-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

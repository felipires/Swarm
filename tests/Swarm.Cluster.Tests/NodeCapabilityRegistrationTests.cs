using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P0-3b: capabilities are reported wholesale on every registration. The
/// Cluster must replace this Node's existing capability rows, not merge —
/// otherwise a Node that removes a handler would leave a stale advertisement
/// behind that dispatch validation (P1-7) would happily accept.
/// </summary>
public class NodeCapabilityRegistrationTests
{
    [Fact]
    public async Task RegisterNode_PersistsAllReportedCapabilities()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();

        await service.RegisterNodeAsync(
            apiKey: "test",
            nodeId: nodeId,
            staticTags: null,
            capabilities: new List<NodeCapability>
            {
                new() { TaskType = "http@1", JsonSchema = "{\"x\":1}" },
                new() { TaskType = "sql@1",  JsonSchema = "{\"y\":2}" },
            });

        var stored = await db.NodeCapabilities.Where(c => c.NodeId == nodeId).OrderBy(c => c.TaskType).ToListAsync();
        stored.Should().HaveCount(2);
        stored[0].TaskType.Should().Be("http@1");
        stored[0].JsonSchema.Should().Be("{\"x\":1}");
        stored[1].TaskType.Should().Be("sql@1");
    }

    [Fact]
    public async Task RegisterNode_ReplacesPreviousCapabilitiesWholesale()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();

        // First registration: 2 capabilities.
        await service.RegisterNodeAsync(
            apiKey: "test", nodeId: nodeId, staticTags: null,
            capabilities: new List<NodeCapability>
            {
                new() { TaskType = "http@1" },
                new() { TaskType = "sql@1" },
            });

        // Second registration: Node has dropped sql@1, added webhook@1.
        await service.RegisterNodeAsync(
            apiKey: "test", nodeId: nodeId, staticTags: null,
            capabilities: new List<NodeCapability>
            {
                new() { TaskType = "http@1" },
                new() { TaskType = "webhook@1" },
            });

        var stored = await db.NodeCapabilities
            .Where(c => c.NodeId == nodeId)
            .Select(c => c.TaskType)
            .OrderBy(t => t)
            .ToListAsync();
        stored.Should().Equal(new[] { "http@1", "webhook@1" },
            "sql@1 must not linger as a stale advertisement after the Node dropped its handler");
    }

    [Fact]
    public async Task RegisterNode_NoCapabilities_LeavesTableEmptyForNode()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();

        await service.RegisterNodeAsync("test", nodeId, staticTags: null, capabilities: null);

        (await db.NodeCapabilities.AnyAsync(c => c.NodeId == nodeId)).Should().BeFalse();
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"caps-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }

    private static NodeService BuildService(ClusterDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Heartbeat:TimeoutSeconds"] = "300",
                ["RabbitMQ:Hostname"] = "x",
                ["RabbitMQ:Port"] = "5672",
                ["RabbitMQ:UserName"] = "x",
                ["RabbitMQ:Password"] = "x",
            })
            .Build();
        return new NodeService(db, NullLogger<NodeService>.Instance, config,
            new Swarm.Cluster.Services.Tags.InMemoryTagMatcher(db));
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Data;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Tags;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P3-3: the denormalized <c>Node.EffectiveTagsJson</c> projection must stay in
/// sync at the two — and only two — tag-write sites (registration sets static;
/// overlay PATCH changes overlay), with static winning on key conflict (D6).
/// </summary>
public class NodeTagSyncTests
{
    [Fact]
    public async Task Register_SetsEffectiveTagsFromStatic()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();

        await service.RegisterNodeAsync("k", nodeId,
            staticTags: new Dictionary<string, string> { ["region"] = "eu" });

        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        node.EffectiveTagsJson.Should().Be("""{"region":"eu"}""");
    }

    [Fact]
    public async Task Register_NoTags_LeavesEffectiveNull()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();

        await service.RegisterNodeAsync("k", nodeId, staticTags: null);

        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        node.EffectiveTagsJson.Should().BeNull();
    }

    [Fact]
    public async Task OverlayAdd_MergesIntoEffective_StaticWins()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();
        await service.RegisterNodeAsync("k", nodeId,
            staticTags: new Dictionary<string, string> { ["region"] = "eu" });

        await service.UpdateOverlayTagsAsync(nodeId,
            add: new Dictionary<string, string> { ["priority"] = "high", ["region"] = "us" },
            remove: null);

        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        // region=eu (static wins over overlay us), priority=high. Canonical order.
        node.EffectiveTagsJson.Should().Be("""{"priority":"high","region":"eu"}""");
    }

    [Fact]
    public async Task OverlayRemove_DropsFromEffective()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();
        await service.RegisterNodeAsync("k", nodeId,
            staticTags: new Dictionary<string, string> { ["region"] = "eu" });
        await service.UpdateOverlayTagsAsync(nodeId,
            add: new Dictionary<string, string> { ["priority"] = "high" }, remove: null);

        await service.UpdateOverlayTagsAsync(nodeId, add: null, remove: new[] { "priority" });

        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        node.EffectiveTagsJson.Should().Be("""{"region":"eu"}""");
    }

    [Fact]
    public async Task Reregister_PreservesOverlayInEffective()
    {
        using var db = BuildDb();
        var service = BuildService(db);
        var nodeId = Guid.NewGuid();
        await service.RegisterNodeAsync("k", nodeId,
            staticTags: new Dictionary<string, string> { ["region"] = "eu" });
        await service.UpdateOverlayTagsAsync(nodeId,
            add: new Dictionary<string, string> { ["priority"] = "high" }, remove: null);

        // Re-register with a different static set — overlay must survive.
        await service.RegisterNodeAsync("k", nodeId,
            staticTags: new Dictionary<string, string> { ["region"] = "us" });

        var node = await db.Nodes.AsNoTracking().SingleAsync(n => n.Id == nodeId);
        node.EffectiveTagsJson.Should().Be("""{"priority":"high","region":"us"}""");
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"tagsync-{Guid.NewGuid()}")
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
        return new NodeService(db, NullLogger<NodeService>.Instance, config, new InMemoryTagMatcher(db));
    }
}

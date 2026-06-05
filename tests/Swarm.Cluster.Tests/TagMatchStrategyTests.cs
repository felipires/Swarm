using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Tags;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Semantic contract for <see cref="ITagMatchStrategy"/>, exercised against the
/// in-memory implementation (the reference). The Postgres `@>` implementation
/// is verified for parity in the integration tier against a real database.
/// </summary>
public class TagMatchStrategyTests
{
    [Fact]
    public async Task MatchEligibleNodes_SelectorSubset_Matches()
    {
        using var db = BuildDb();
        var node = await SeedNodeAsync(db, online: true, effective: new() { ["region"] = "eu", ["team"] = "data" }, taskType: "http@1");
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchEligibleNodesAsync(
            new Dictionary<string, string> { ["region"] = "eu" }, "http@1", default);

        result.Should().ContainSingle().Which.Should().Be(node.Id);
    }

    [Fact]
    public async Task MatchEligibleNodes_OverSpecifiedSelector_NoMatch()
    {
        using var db = BuildDb();
        await SeedNodeAsync(db, online: true, effective: new() { ["region"] = "eu" }, taskType: "http@1");
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchEligibleNodesAsync(
            new Dictionary<string, string> { ["region"] = "eu", ["team"] = "data" }, "http@1", default);

        result.Should().BeEmpty("the Node's tags are not a superset of the selector");
    }

    [Fact]
    public async Task MatchEligibleNodes_OfflineNode_Excluded()
    {
        using var db = BuildDb();
        await SeedNodeAsync(db, online: false, effective: new() { ["region"] = "eu" }, taskType: "http@1");
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchEligibleNodesAsync(
            new Dictionary<string, string> { ["region"] = "eu" }, "http@1", default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchEligibleNodes_CapabilityMissing_Excluded()
    {
        using var db = BuildDb();
        await SeedNodeAsync(db, online: true, effective: new() { ["region"] = "eu" }, taskType: "sql@1");
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchEligibleNodesAsync(
            new Dictionary<string, string> { ["region"] = "eu" }, "http@1", default);

        result.Should().BeEmpty("the Node advertises sql@1, not http@1");
    }

    [Fact]
    public async Task MatchEligibleNodes_FallsBackToStaticWhenEffectiveNull()
    {
        // Older seed: only StaticTagsJson set, EffectiveTagsJson null. The
        // matcher recomputes on the fly.
        using var db = BuildDb();
        var node = new Node
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Status = Node.NodeStatus.Online,
            StaticTagsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "eu" }),
            EffectiveTagsJson = null,
        };
        db.Nodes.Add(node);
        db.NodeCapabilities.Add(Cap(node.Id, "http@1"));
        await db.SaveChangesAsync();
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchEligibleNodesAsync(
            new Dictionary<string, string> { ["region"] = "eu" }, "http@1", default);

        result.Should().ContainSingle().Which.Should().Be(node.Id);
    }

    [Fact]
    public async Task MatchRoutesForNode_ReturnsOnlySubsetRoutes()
    {
        using var db = BuildDb();
        var node = await SeedNodeAsync(db, online: true, effective: new() { ["region"] = "eu", ["team"] = "data" }, taskType: "http@1");

        var matchHash = TaggedRouteHash.Compute(new Dictionary<string, string> { ["region"] = "eu" }).Hash;
        var noMatchHash = TaggedRouteHash.Compute(new Dictionary<string, string> { ["region"] = "us" }).Hash;
        db.TaggedRoutes.AddRange(
            new TaggedRoute { Hash = matchHash, SelectorJson = """{"region":"eu"}""", FirstSeenAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow },
            new TaggedRoute { Hash = noMatchHash, SelectorJson = """{"region":"us"}""", FirstSeenAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var matcher = new InMemoryTagMatcher(db);

        var result = await matcher.MatchRoutesForNodeAsync(node.Id, default);

        result.Should().ContainSingle().Which.Should().Be(matchHash);
    }

    [Fact]
    public async Task MatchRoutesForNode_UnknownNode_Empty()
    {
        using var db = BuildDb();
        var matcher = new InMemoryTagMatcher(db);
        var result = await matcher.MatchRoutesForNodeAsync(Guid.NewGuid(), default);
        result.Should().BeEmpty();
    }

    private static async Task<Node> SeedNodeAsync(
        ClusterDbContext db, bool online, Dictionary<string, string> effective, string taskType)
    {
        var node = new Node
        {
            Id = Guid.NewGuid(),
            Name = "n",
            Status = online ? Node.NodeStatus.Online : Node.NodeStatus.Offline,
            StaticTagsJson = JsonSerializer.Serialize(effective),
            EffectiveTagsJson = Swarm.Cluster.Services.Tags.EffectiveTags.Serialize(effective),
        };
        db.Nodes.Add(node);
        db.NodeCapabilities.Add(Cap(node.Id, taskType));
        await db.SaveChangesAsync();
        return node;
    }

    private static NodeCapability Cap(Guid nodeId, string taskType) => new()
    {
        Id = Guid.NewGuid(),
        NodeId = nodeId,
        TaskType = taskType,
        JsonSchema = "{}",
        RequiredEnvKeysJson = "[]",
        RequiredParamsJson = "[]",
        ReportedAt = DateTime.UtcNow,
    };

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"tagmatch-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

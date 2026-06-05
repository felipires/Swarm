using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services.Tags;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Integration-tier parity check (roadmap testing tiers): the GIN-indexed
/// Postgres <c>@&gt;</c> matcher must return the same sets as the in-memory
/// reference for the same fixture. Runs only when <c>SWARM_TEST_POSTGRES_CONN</c>
/// points at a reachable, migrated database — otherwise every fact no-ops, so
/// the normal unit loop stays green without infrastructure.
/// </summary>
[Trait("Category", "Integration")]
public class PostgresJsonbTagMatcherParityTests
{
    private static string? ConnString => Environment.GetEnvironmentVariable("SWARM_TEST_POSTGRES_CONN");

    [Fact]
    public async Task EligibleNodes_PostgresMatchesInMemory()
    {
        if (string.IsNullOrWhiteSpace(ConnString)) return; // skipped: no DB configured

        await using var db = BuildDb(ConnString);
        await db.Database.MigrateAsync();
        await ResetAsync(db);

        var eu = SeedNode(db, online: true, tags: new() { ["region"] = "eu", ["team"] = "data" }, taskType: "http@1");
        SeedNode(db, online: true, tags: new() { ["region"] = "us" }, taskType: "http@1");
        SeedNode(db, online: false, tags: new() { ["region"] = "eu" }, taskType: "http@1");
        await db.SaveChangesAsync();

        var selector = new Dictionary<string, string> { ["region"] = "eu" };
        var pg = await new PostgresJsonbTagMatcher(db).MatchEligibleNodesAsync(selector, "http@1", default);
        var mem = await new InMemoryTagMatcher(db).MatchEligibleNodesAsync(selector, "http@1", default);

        pg.Should().BeEquivalentTo(mem);
        pg.Should().ContainSingle().Which.Should().Be(eu);
    }

    [Fact]
    public async Task RoutesForNode_PostgresMatchesInMemory()
    {
        if (string.IsNullOrWhiteSpace(ConnString)) return; // skipped: no DB configured

        await using var db = BuildDb(ConnString);
        await db.Database.MigrateAsync();
        await ResetAsync(db);

        var node = SeedNode(db, online: true, tags: new() { ["region"] = "eu", ["team"] = "data" }, taskType: "http@1");
        var match = "1111111111111111";
        var noMatch = "2222222222222222";
        db.TaggedRoutes.AddRange(
            new TaggedRoute { Hash = match, SelectorJson = """{"region":"eu"}""", FirstSeenAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow },
            new TaggedRoute { Hash = noMatch, SelectorJson = """{"region":"us"}""", FirstSeenAt = DateTime.UtcNow, LastUsedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var pg = await new PostgresJsonbTagMatcher(db).MatchRoutesForNodeAsync(node, default);
        var mem = await new InMemoryTagMatcher(db).MatchRoutesForNodeAsync(node, default);

        pg.Should().BeEquivalentTo(mem);
        pg.Should().ContainSingle().Which.Should().Be(match);
    }

    private static Guid SeedNode(ClusterDbContext db, bool online, Dictionary<string, string> tags, string taskType)
    {
        var id = Guid.NewGuid();
        db.Nodes.Add(new Node
        {
            Id = id,
            Name = "n",
            Status = online ? Node.NodeStatus.Online : Node.NodeStatus.Offline,
            StaticTagsJson = JsonSerializer.Serialize(tags),
            EffectiveTagsJson = EffectiveTags.Serialize(tags),
        });
        db.NodeCapabilities.Add(new NodeCapability
        {
            Id = Guid.NewGuid(),
            NodeId = id,
            TaskType = taskType,
            JsonSchema = "{}",
            RequiredEnvKeysJson = "[]",
            RequiredParamsJson = "[]",
            ReportedAt = DateTime.UtcNow,
        });
        return id;
    }

    private static async Task ResetAsync(ClusterDbContext db)
    {
        db.NodeCapabilities.RemoveRange(db.NodeCapabilities);
        db.TaggedRoutes.RemoveRange(db.TaggedRoutes);
        db.Nodes.RemoveRange(db.Nodes);
        await db.SaveChangesAsync();
    }

    private static ClusterDbContext BuildDb(string conn)
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new ClusterDbContext(opts);
    }
}

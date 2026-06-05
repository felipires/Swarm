using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Integration-tier test for <see cref="TaggedRouteRetentionService"/>. The
/// service deletes via raw SQL on an <see cref="NpgsqlDataSource"/>, which EF
/// InMemory can't execute — so this runs only when
/// <c>SWARM_TEST_POSTGRES_CONN</c> points at a reachable, migrated database
/// (otherwise every fact no-ops, keeping the unit loop green without infra),
/// mirroring <see cref="PostgresJsonbTagMatcherParityTests"/>.
/// </summary>
[Trait("Category", "Integration")]
public class TaggedRouteRetentionTests
{
    private static string? ConnString => Environment.GetEnvironmentVariable("SWARM_TEST_POSTGRES_CONN");

    [Fact]
    public async Task TrimOnce_DeletesOnlyStaleRoutes()
    {
        if (string.IsNullOrWhiteSpace(ConnString)) return; // skipped: no DB configured

        await using var db = BuildDb(ConnString);
        await db.Database.MigrateAsync();
        await ResetAsync(db);

        var now = DateTime.UtcNow;
        db.TaggedRoutes.AddRange(
            new TaggedRoute { Hash = "staleaaaaaaaaaaa", SelectorJson = """{"region":"eu"}""", FirstSeenAt = now.AddDays(-60), LastUsedAt = now.AddDays(-40) },
            new TaggedRoute { Hash = "freshbbbbbbbbbbb", SelectorJson = """{"region":"us"}""", FirstSeenAt = now.AddDays(-60), LastUsedAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var deleted = await BuildService(ConnString, retentionDays: 30).TrimOnceAsync(default);

        deleted.Should().Be(1);
        var remaining = await db.TaggedRoutes.AsNoTracking().Select(r => r.Hash).ToListAsync();
        remaining.Should().ContainSingle().Which.Should().Be("freshbbbbbbbbbbb");
    }

    [Fact]
    public async Task TrimOnce_RetentionDisabled_DeletesNothing()
    {
        if (string.IsNullOrWhiteSpace(ConnString)) return; // skipped: no DB configured

        await using var db = BuildDb(ConnString);
        await db.Database.MigrateAsync();
        await ResetAsync(db);

        db.TaggedRoutes.Add(new TaggedRoute
        {
            Hash = "ancientcccccccc",
            SelectorJson = """{"region":"eu"}""",
            FirstSeenAt = DateTime.UtcNow.AddYears(-1),
            LastUsedAt = DateTime.UtcNow.AddYears(-1),
        });
        await db.SaveChangesAsync();

        // RetentionDays <= 0 disables: the service ExecuteAsync no-ops, and a
        // direct TrimOnce with a non-positive window would compute a future
        // cutoff — but the guard lives in ExecuteAsync, so we assert the
        // configured service does not run a sweep at all.
        var svc = BuildService(ConnString, retentionDays: 0);
        // ExecuteAsync returns immediately when disabled; nothing deleted.
        await svc.StartAsync(default);
        await svc.StopAsync(default);

        (await db.TaggedRoutes.AsNoTracking().AnyAsync(r => r.Hash == "ancientcccccccc"))
            .Should().BeTrue("retention is disabled, so the ancient route survives");
    }

    private static TaggedRouteRetentionService BuildService(string conn, int retentionDays)
    {
        var dataSource = NpgsqlDataSource.Create(conn);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaggedRoutes:RetentionDays"] = retentionDays.ToString(),
            ["TaggedRoutes:RetentionCheckIntervalHours"] = "6",
        }).Build();
        return new TaggedRouteRetentionService(dataSource, config, NullLogger<TaggedRouteRetentionService>.Instance);
    }

    private static async Task ResetAsync(ClusterDbContext db)
    {
        db.TaggedRoutes.RemoveRange(db.TaggedRoutes);
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

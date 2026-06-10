using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Controllers;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Log search endpoint (observability v2). Covers the provider-translatable
/// filters (node, level, time range) and the (Timestamp DESC, Id DESC) keyset
/// cursor walk. Tag-containment (<c>Tags @&gt;</c>) and free-text ILIKE rely on
/// Postgres operators and are exercised by integration/manual verification, not
/// the EF InMemory provider used here.
/// </summary>
public class LogSearchTests
{
    [Fact]
    public async Task Search_NewestFirst_AndCursorWalkCoversAllOnce()
    {
        using var db = BuildDb();
        for (var i = 0; i < 5; i++)
            db.Logs.Add(NewLog(ts: new DateTime(2026, 6, 5, 10, i, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var controller = new LogsController(db);

        var first = Unwrap(await Search(controller, limit: 2));
        first.Items.Select(x => x.Timestamp).Should().BeInDescendingOrder();

        var seen = new List<Guid>();
        string? after = null;
        bool hasMore;
        do
        {
            var page = Unwrap(await Search(controller, after: after, limit: 2));
            seen.AddRange(page.Items.Select(x => x.Id));
            after = page.NextCursor;
            hasMore = page.HasMore;
        } while (hasMore);

        seen.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Search_FiltersByNode()
    {
        using var db = BuildDb();
        var nodeA = Guid.NewGuid();
        db.Logs.Add(NewLog(nodeId: nodeA));
        db.Logs.Add(NewLog(nodeId: Guid.NewGuid()));
        var clusterOrigin = NewLog();
        clusterOrigin.NodeId = null; // cluster-origin lifecycle log
        db.Logs.Add(clusterOrigin);
        await db.SaveChangesAsync();
        var controller = new LogsController(db);

        var page = Unwrap(await Search(controller, nodeId: nodeA));
        page.Items.Should().ContainSingle().Which.NodeId.Should().Be(nodeA);
    }

    [Fact]
    public async Task Search_FiltersByLevelSet()
    {
        using var db = BuildDb();
        db.Logs.Add(NewLog(level: "Information"));
        db.Logs.Add(NewLog(level: "Warning"));
        db.Logs.Add(NewLog(level: "Error"));
        await db.SaveChangesAsync();
        var controller = new LogsController(db);

        var page = Unwrap(await Search(controller, level: new[] { "Warning", "Error" }));
        page.Items.Should().HaveCount(2);
        page.Items.Select(x => x.Level).Should().OnlyContain(l => l == "Warning" || l == "Error");
    }

    [Fact]
    public async Task Search_FiltersByTimeRange()
    {
        using var db = BuildDb();
        db.Logs.Add(NewLog(ts: new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc)));
        db.Logs.Add(NewLog(ts: new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var controller = new LogsController(db);

        var page = Unwrap(await Search(controller,
            from: new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));

        page.Items.Should().ContainSingle()
            .Which.Timestamp.Should().Be(new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Search_BadCursor_Returns400()
    {
        using var db = BuildDb();
        var controller = new LogsController(db);

        var result = await Search(controller, after: "garbage");

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ApiError>()
            .Which.Code.Should().Be("INVALID_CURSOR");
    }

    [Fact]
    public async Task Search_BadTag_Returns400()
    {
        using var db = BuildDb();
        var controller = new LogsController(db);

        var result = await Search(controller, tags: new[] { "notakeyvalue" });

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ApiError>()
            .Which.Code.Should().Be("INVALID_TAG");
    }

    // ---- helpers ------------------------------------------------------------

    private static Task<ActionResult> Search(
        LogsController controller,
        Guid? nodeId = null,
        string[]? tags = null,
        string[]? level = null,
        string? q = null,
        DateTime? from = null,
        DateTime? to = null,
        string? after = null,
        int? limit = null)
        => controller.Search(nodeId, tags, level, q, from, to, after, limit, CancellationToken.None);

    private static CursorPagedResult<LogResponse> Unwrap(ActionResult result)
        => result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<CursorPagedResult<LogResponse>>().Subject;

    private static Log NewLog(
        Guid? nodeId = null,
        string level = "Information",
        DateTime? ts = null) => new()
    {
        Id = Guid.NewGuid(),
        NodeId = nodeId ?? Guid.NewGuid(),
        Level = level,
        MessageTemplate = "hello {Name}",
        Message = "hello world",
        Timestamp = ts ?? DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"logs-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

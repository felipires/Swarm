using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Cluster.Controllers;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P3-1 cursor (keyset) pagination: the opaque cursor codec round-trips and
/// rejects garbage, the limit clamps to the shared window, and a cursor walk
/// over <c>GET /api/tasks/{id}/instances</c> covers every row exactly once even
/// when new rows arrive mid-scroll.
/// </summary>
public class CursorPaginationTests
{
    // ---- codec --------------------------------------------------------------

    [Fact]
    public void Cursor_RoundTrips()
    {
        var key = new Cursor.Key(new DateTime(2026, 6, 5, 10, 30, 0, DateTimeKind.Utc), Guid.NewGuid());
        var token = Cursor.Encode(key);
        Cursor.TryDecode(token, out var decoded).Should().BeTrue();
        decoded.CreatedAt.Should().Be(key.CreatedAt);
        decoded.Id.Should().Be(key.Id);
    }

    [Fact]
    public void Cursor_TokenIsUrlSafe()
    {
        var token = Cursor.Encode(new Cursor.Key(DateTime.UtcNow, Guid.NewGuid()));
        token.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("bm9jb2xvbg")]   // base64 of "nocolon" — no ':' separator
    public void Cursor_Malformed_ReturnsFalse(string token)
    {
        Cursor.TryDecode(token, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(50, 50)]
    [InlineData(201, 200)]
    [InlineData(10_000, 200)]
    public void CursorRequest_NormalizedLimit_ClampsToWindow(int input, int expected)
    {
        new CursorRequest { Limit = input }.NormalizedLimit.Should().Be(expected);
    }

    // ---- controller walk ----------------------------------------------------

    [Fact]
    public async Task GetInstances_CursorWalk_CoversAllRowsOnceNoGaps()
    {
        using var db = BuildDb();
        var defId = await SeedDefinitionAsync(db);
        // 5 instances, strictly increasing CreatedAt.
        for (var i = 0; i < 5; i++)
            db.TaskInstances.Add(NewInstance(defId, new DateTime(2026, 6, 5, 10, i, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var controller = BuildController(db);

        var seen = new List<Guid>();
        string? after = null;
        bool hasMore;
        do
        {
            var result = await controller.GetInstances(defId,
                new PageRequest(), new CursorRequest { After = after, Limit = 2 }, useCursor: true);
            var paged = Unwrap(result);
            seen.AddRange(paged.Items.Select(x => x.Id));
            after = paged.NextCursor;
            hasMore = paged.HasMore;
        } while (hasMore);

        seen.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task GetInstances_CursorWalk_DescendingByCreatedAt()
    {
        using var db = BuildDb();
        var defId = await SeedDefinitionAsync(db);
        for (var i = 0; i < 3; i++)
            db.TaskInstances.Add(NewInstance(defId, new DateTime(2026, 6, 5, 10, i, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var controller = BuildController(db);

        var first = Unwrap(await controller.GetInstances(defId,
            new PageRequest(), new CursorRequest { Limit = 10 }, useCursor: true));

        first.HasMore.Should().BeFalse();
        first.NextCursor.Should().BeNull();
        first.Items.Select(x => x.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetInstances_InsertMidScroll_DoesNotShiftWalk()
    {
        using var db = BuildDb();
        var defId = await SeedDefinitionAsync(db);
        for (var i = 0; i < 4; i++)
            db.TaskInstances.Add(NewInstance(defId, new DateTime(2026, 6, 5, 10, i, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var controller = BuildController(db);

        var p1 = Unwrap(await controller.GetInstances(defId,
            new PageRequest(), new CursorRequest { Limit = 2 }, useCursor: true));

        // A newer row arrives between page 1 and page 2. Keyset paging anchors on
        // the cursor, so the second page is unaffected by the new head row.
        db.TaskInstances.Add(NewInstance(defId, new DateTime(2026, 6, 5, 11, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var p2 = Unwrap(await controller.GetInstances(defId,
            new PageRequest(), new CursorRequest { After = p1.NextCursor, Limit = 2 }, useCursor: true));

        p1.Items.Select(x => x.Id).Should().NotIntersectWith(p2.Items.Select(x => x.Id));
        p2.Items.Should().HaveCount(2, "the two oldest rows, untouched by the new head insert");
    }

    [Fact]
    public async Task GetInstances_BadCursor_Returns400()
    {
        using var db = BuildDb();
        var defId = await SeedDefinitionAsync(db);
        var controller = BuildController(db);

        var result = await controller.GetInstances(defId,
            new PageRequest(), new CursorRequest { After = "garbage" }, useCursor: true);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ApiError>()
            .Which.Code.Should().Be("INVALID_CURSOR");
    }

    [Fact]
    public async Task GetInstances_DefaultMode_StillOffset()
    {
        using var db = BuildDb();
        var defId = await SeedDefinitionAsync(db);
        db.TaskInstances.Add(NewInstance(defId, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var controller = BuildController(db);

        var result = await controller.GetInstances(defId, new PageRequest(), new CursorRequest());

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<PagedResult<TaskInstanceResponse>>();
    }

    // ---- helpers ------------------------------------------------------------

    private static CursorPagedResult<TaskInstanceResponse> Unwrap(ActionResult result)
        => result.Should().BeOfType<OkObjectResult>().Subject
            .Value.Should().BeOfType<CursorPagedResult<TaskInstanceResponse>>().Subject;

    private static TaskInstance NewInstance(Guid defId, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        TaskDefinitionId = defId,
        CreatedAt = createdAt,
        TaskType = "default@1",
        ConfigJsonSnapshot = "{}",
    };

    private static async Task<Guid> SeedDefinitionAsync(ClusterDbContext db)
    {
        var def = new TaskDefinition { Id = Guid.NewGuid(), Name = "t", TaskType = "default@1" };
        db.TaskDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.Id;
    }

    private static TasksController BuildController(ClusterDbContext db)
    {
        var dispatch = new TaskDispatchService(db, NullLogger<TaskDispatchService>.Instance);
        return new TasksController(db, dispatch, new EntityVersionService(db), NullLogger<TasksController>.Instance);
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"cursor-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

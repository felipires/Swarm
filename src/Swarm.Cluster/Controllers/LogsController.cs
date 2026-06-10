using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly ClusterDbContext _db;

    public LogsController(ClusterDbContext db) => _db = db;

    /// <summary>
    /// Persistent, paginated log search (observability v2 — replaces SSE
    /// streaming). Filters: <c>nodeId</c>; repeated <c>tags=key:value</c>
    /// (AND-combined jsonb containment, incl. arbitrary keys like
    /// <c>env.DB:prod</c>); repeated <c>level</c>; <c>q</c> free text
    /// (substring over message and template, pg_trgm-backed); <c>from</c>/<c>to</c>
    /// UTC range. Ordered newest-first with an opaque <c>(Timestamp, Id)</c>
    /// keyset cursor (<c>after</c>).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> Search(
        [FromQuery] Guid? nodeId,
        [FromQuery(Name = "tags")] string[]? tags,
        [FromQuery(Name = "level")] string[]? level,
        [FromQuery] string? q,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? after,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var (query, badRequest) = ApplyFilters(_db.Logs, nodeId, tags, level, q, from, to);
        if (badRequest is not null) return badRequest;

        if (!string.IsNullOrWhiteSpace(after))
        {
            if (!Cursor.TryDecode(after, out var key))
                return BadRequest(new ApiError("INVALID_CURSOR", "The 'after' cursor is malformed"));
            query = query.Where(l =>
                l.Timestamp < key.CreatedAt
                || (l.Timestamp == key.CreatedAt && l.Id.CompareTo(key.Id) < 0));
        }

        var take = limit is > 0 and <= PageRequest.MaxPageSize
            ? limit.Value
            : PageRequest.DefaultPageSize;

        var rows = await query
            .OrderByDescending(l => l.Timestamp)
            .ThenByDescending(l => l.Id)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        var page = hasMore ? rows.Take(take).ToList() : rows;
        var nextCursor = hasMore && page.Count > 0
            ? Cursor.Encode(new Cursor.Key(page[^1].Timestamp, page[^1].Id))
            : null;

        return Ok(new CursorPagedResult<LogResponse>(
            page.Select(LogResponse.From).ToList(), nextCursor, hasMore));
    }

    /// <summary>
    /// Returns the count of log rows matching the given filters. Intended for
    /// dashboard widgets (e.g. alert badge) that need a number without fetching
    /// full log objects. Accepts the same filter params as <c>GET /api/logs</c>
    /// except <c>after</c> and <c>limit</c>.
    /// </summary>
    [HttpGet("count")]
    public async Task<ActionResult> Count(
        [FromQuery] Guid? nodeId,
        [FromQuery(Name = "tags")] string[]? tags,
        [FromQuery(Name = "level")] string[]? level,
        [FromQuery] string? q,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        var (query, badRequest) = ApplyFilters(_db.Logs, nodeId, tags, level, q, from, to);
        if (badRequest is not null) return badRequest;

        var count = await query.CountAsync(cancellationToken);
        return Ok(new { count });
    }

    // Shared filter logic for Search and Count.
    private (IQueryable<Log> Query, BadRequestObjectResult? Error) ApplyFilters(
        IQueryable<Log> query,
        Guid? nodeId,
        string[]? tags,
        string[]? level,
        string? q,
        DateTime? from,
        DateTime? to)
    {
        if (nodeId is { } nid)
            query = query.Where(l => l.NodeId == nid);

        if (level is { Length: > 0 })
            query = query.Where(l => level.Contains(l.Level));

        if (from is { } f)
        {
            var fUtc = DateTime.SpecifyKind(f, DateTimeKind.Utc);
            query = query.Where(l => l.Timestamp >= fUtc);
        }
        if (to is { } t)
        {
            var tUtc = DateTime.SpecifyKind(t, DateTimeKind.Utc);
            query = query.Where(l => l.Timestamp <= tUtc);
        }

        if (tags is { Length: > 0 })
        {
            var selector = new Dictionary<string, string>();
            foreach (var pair in tags)
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                var sep = pair.IndexOf(':');
                if (sep <= 0 || sep == pair.Length - 1)
                    return (query, BadRequest(new ApiError("INVALID_TAG", $"Tag '{pair}' must be in key:value form")));
                selector[pair[..sep]] = pair[(sep + 1)..];
            }
            if (selector.Count > 0)
            {
                var selectorJson = JsonSerializer.Serialize(selector);
                query = query.Where(l => l.Tags != null && EF.Functions.JsonContains(l.Tags, selectorJson));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{EscapeLike(q.Trim())}%";
            query = query.Where(l =>
                (l.Message != null && EF.Functions.ILike(l.Message, like))
                || EF.Functions.ILike(l.MessageTemplate, like));
        }

        return (query, null);
    }

    /// <summary>Escape LIKE/ILIKE wildcards so user text is matched literally.</summary>
    private static string EscapeLike(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

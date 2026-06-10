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
        IQueryable<Log> query = _db.Logs;

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

        // Tag facets → a single jsonb containment predicate `Tags @> @json`,
        // served by the GIN(jsonb_path_ops) index. Supports arbitrary keys.
        if (tags is { Length: > 0 })
        {
            var selector = new Dictionary<string, string>();
            foreach (var pair in tags)
            {
                if (string.IsNullOrWhiteSpace(pair)) continue;
                var sep = pair.IndexOf(':');
                if (sep <= 0 || sep == pair.Length - 1)
                    return BadRequest(new ApiError("INVALID_TAG", $"Tag '{pair}' must be in key:value form"));
                selector[pair[..sep]] = pair[(sep + 1)..];
            }
            if (selector.Count > 0)
            {
                var selectorJson = JsonSerializer.Serialize(selector);
                query = query.Where(l => l.Tags != null && EF.Functions.JsonContains(l.Tags, selectorJson));
            }
        }

        // Free text → substring over message and template (pg_trgm GIN ILIKE).
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{EscapeLike(q.Trim())}%";
            query = query.Where(l =>
                (l.Message != null && EF.Functions.ILike(l.Message, like))
                || EF.Functions.ILike(l.MessageTemplate, like));
        }

        if (!string.IsNullOrWhiteSpace(after))
        {
            if (!Cursor.TryDecode(after, out var key))
                return BadRequest(new ApiError("INVALID_CURSOR", "The 'after' cursor is malformed"));
            // Keyset boundary under (Timestamp DESC, Id DESC). Expanded form for
            // reliable provider translation.
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
            .Take(take + 1) // one extra to detect HasMore
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        var page = hasMore ? rows.Take(take).ToList() : rows;
        var nextCursor = hasMore && page.Count > 0
            ? Cursor.Encode(new Cursor.Key(page[^1].Timestamp, page[^1].Id))
            : null;

        return Ok(new CursorPagedResult<LogResponse>(
            page.Select(LogResponse.From).ToList(), nextCursor, hasMore));
    }

    /// <summary>Escape LIKE/ILIKE wildcards so user text is matched literally.</summary>
    private static string EscapeLike(string input) =>
        input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

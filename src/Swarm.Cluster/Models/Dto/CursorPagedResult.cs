using System.Buffers.Text;
using System.Text;

namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// Cursor- (keyset-) paginated list response, the deferred half of P3-1 for
/// high-frequency endpoints. Unlike <see cref="PagedResult{T}"/> there is no
/// total count — keyset pagination trades it away for stable, index-friendly
/// deep paging. <see cref="NextCursor"/> is null when <see cref="HasMore"/> is
/// false.
/// </summary>
public record CursorPagedResult<T>(List<T> Items, string? NextCursor, bool HasMore);

/// <summary>
/// Query-string-bound cursor pagination parameters. <c>after</c> is an opaque
/// token from a previous response's <c>NextCursor</c> (null/absent = first
/// page). <c>limit</c> shares the clamp window with <see cref="PageRequest"/>.
/// </summary>
public class CursorRequest
{
    public string? After { get; set; }
    public int Limit { get; set; } = PageRequest.DefaultPageSize;

    public int NormalizedLimit
    {
        get
        {
            if (Limit <= 0) return PageRequest.DefaultPageSize;
            if (Limit > PageRequest.MaxPageSize) return PageRequest.MaxPageSize;
            return Limit;
        }
    }
}

/// <summary>
/// Opaque codec for a <c>(CreatedAt, Id)</c> keyset cursor. The token is
/// URL-safe Base64 over <c>"{unixMillis}:{guid}"</c> — opaque to clients (they
/// only echo it back), stable across requests, and cheap to validate.
/// </summary>
public static class Cursor
{
    public readonly record struct Key(DateTime CreatedAt, Guid Id);

    public static string Encode(Key key)
    {
        var millis = new DateTimeOffset(DateTime.SpecifyKind(key.CreatedAt, DateTimeKind.Utc))
            .ToUnixTimeMilliseconds();
        var raw = $"{millis}:{key.Id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decode a token. Returns false (rather than throwing) on any malformed
    /// input so callers can map cleanly to a 400.
    /// </summary>
    public static bool TryDecode(string token, out Key key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var sep = raw.IndexOf(':');
            if (sep <= 0) return false;
            if (!long.TryParse(raw.AsSpan(0, sep), out var millis)) return false;
            if (!Guid.TryParseExact(raw.AsSpan(sep + 1), "N", out var id)) return false;
            key = new Key(DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime, id);
            return true;
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}

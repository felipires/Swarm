using System.Text.Json;
using System.Text.RegularExpressions;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Walks a dot/bracket path (e.g. <c>rows[0].email</c>) into a
/// <see cref="JsonElement"/> and returns the value as a string suitable
/// for injection into runtime params.
/// </summary>
internal static partial class OutputMappingPathExtractor
{
    private static readonly char[] Separators = ['.'];

    /// <summary>
    /// Extract a value from <paramref name="root"/> at <paramref name="path"/>.
    /// Returns null when any segment is missing or the path is out of range.
    /// An empty path returns the raw JSON of the entire element.
    /// </summary>
    public static string? Extract(JsonElement root, string path)
    {
        if (string.IsNullOrEmpty(path))
            return ElementToString(root);

        var current = root;
        foreach (var segment in SplitPath(path))
        {
            if (segment.Index is int idx)
            {
                if (current.ValueKind != JsonValueKind.Array) return null;
                if (idx < 0 || idx >= current.GetArrayLength()) return null;
                current = current[idx];
            }
            else
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(segment.Key!, out current)) return null;
            }
        }

        return ElementToString(current);
    }

    private static string ElementToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Undefined => string.Empty,
        _ => element.GetRawText(),
    };

    private static IEnumerable<PathSegment> SplitPath(string path)
    {
        // Split on '.' but keep bracket groups together, e.g.:
        //   "rows[0].email" → ["rows[0]", "email"]
        //   "data.items[2].id" → ["data", "items[2]", "id"]
        foreach (var part in path.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = part.IndexOf('[');
            if (bracket < 0)
            {
                yield return new PathSegment(part, null);
                continue;
            }

            // e.g. "rows[0]" → key="rows", then index=0
            var key = part[..bracket];
            if (!string.IsNullOrEmpty(key))
                yield return new PathSegment(key, null);

            var m = IndexPattern().Match(part, bracket);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var idx))
                yield return new PathSegment(null, idx);
        }
    }

    [GeneratedRegex(@"\[(\d+)\]")]
    private static partial Regex IndexPattern();

    private readonly record struct PathSegment(string? Key, int? Index);
}

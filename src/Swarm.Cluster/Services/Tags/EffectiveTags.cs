using System.Text.Json;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Tags;

/// <summary>
/// Computes a Node's <em>effective</em> tag set — the merge of its static
/// tags (<see cref="Node.StaticTagsJson"/>) and its Cluster-managed overlay
/// tags (<see cref="NodeOverlayTag"/>), with the static value winning on key
/// conflict (D6).
///
/// The serialized form is canonical (keys sorted case-insensitively, no
/// whitespace) so equal tag sets always produce byte-identical jsonb — this
/// matches the convention in <see cref="TaggedRouteHash"/> so a selector and
/// an effective-tag projection compare cleanly under Postgres <c>@&gt;</c>.
///
/// This is the single definition of the merge semantics. It is invoked at the
/// two — and only two — sites that change a Node's tags
/// (<c>NodeService.RegisterNodeAsync</c>, <c>NodeService.UpdateOverlayTagsAsync</c>)
/// to keep <see cref="Node.EffectiveTagsJson"/> in sync, and by the in-memory
/// tag matcher as its semantic reference.
/// </summary>
public static class EffectiveTags
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <summary>
    /// Merge static + overlay into the effective set. Static wins on conflict
    /// (D6). Keys are compared case-insensitively. Malformed
    /// <paramref name="staticTagsJson"/> is treated as empty.
    /// </summary>
    public static Dictionary<string, string> Compose(string? staticTagsJson, IEnumerable<NodeOverlayTag> overlay)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in overlay)
            merged[t.Key] = t.Value;                      // overlay first
        foreach (var (k, v) in ParseStatic(staticTagsJson))
            merged[k] = v;                                // static wins (D6)
        return merged;
    }

    /// <summary>
    /// Canonical jsonb text for an effective-tag set. Returns <c>null</c> for
    /// an empty set so the column stays NULL rather than <c>{}</c> — a NULL
    /// effective-tags row can never satisfy a non-empty selector, which is the
    /// correct semantics for an untagged Node.
    /// </summary>
    public static string? Serialize(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.Count == 0) return null;
        var sorted = tags
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return JsonSerializer.Serialize(sorted, JsonOpts);
    }

    /// <summary>Convenience: compose then serialize.</summary>
    public static string? ComposeJson(string? staticTagsJson, IEnumerable<NodeOverlayTag> overlay)
        => Serialize(Compose(staticTagsJson, overlay));

    /// <summary>Parse a stored effective/static tags blob; empty on null/garbage.</summary>
    public static Dictionary<string, string> Parse(string? tagsJson)
    {
        var result = ParseStatic(tagsJson);
        return new Dictionary<string, string>(result, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseStatic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

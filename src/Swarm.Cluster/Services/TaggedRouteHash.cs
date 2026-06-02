using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Swarm.Cluster.Services;

/// <summary>
/// Deterministic identifier for a tag selector used in
/// <see cref="Models.DispatchStrategy.TaggedNodes"/> routing. Both sides
/// (Cluster picking the queue, Node deciding what to subscribe to) compute
/// the same hash so neither needs an out-of-band naming convention.
///
/// Format: SHA-256 of the canonical JSON of the selector (keys sorted
/// case-insensitively), then the first 16 hex chars. 16 hex = 64 bits of
/// uniqueness — collisions are operationally infeasible without billions of
/// distinct selectors.
/// </summary>
public static class TaggedRouteHash
{
    public static (string Hash, string CanonicalJson) Compute(IReadOnlyDictionary<string, string> selector)
    {
        // Canonical form: sort keys, render as JSON object with no whitespace.
        var sorted = selector
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var canonical = JsonSerializer.Serialize(sorted, JsonOpts);
        var hash = ComputeHash(canonical);
        return (hash, canonical);
    }

    public static string QueueNameFor(IReadOnlyDictionary<string, string> selector)
        => $"tasks.tagged.{Compute(selector).Hash}";

    public static string QueueNameForHash(string hash) => $"tasks.tagged.{hash}";

    private static string ComputeHash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };
}

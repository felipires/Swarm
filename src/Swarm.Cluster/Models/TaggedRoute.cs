namespace Swarm.Cluster.Models;

/// <summary>
/// Records a tag selector that has been used to dispatch via
/// <see cref="DispatchStrategy.TaggedNodes"/>. The <c>Hash</c> is a
/// deterministic SHA-256 of the canonical (key-sorted) JSON of the selector
/// — it forms the routing-key suffix for the shared queue
/// <c>tasks.tagged.&lt;hash&gt;</c>. Nodes whose effective tags are a
/// superset of <see cref="SelectorJson"/> are told to subscribe to that
/// queue via the next heartbeat response (P0-3a / D6).
/// </summary>
public class TaggedRoute
{
    /// <summary>SHA-256 of canonical selector JSON, truncated to 16 hex chars.</summary>
    public string Hash { get; set; } = null!;

    /// <summary>Canonical JSON encoding of the tag selector map.</summary>
    public string SelectorJson { get; set; } = null!;

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}

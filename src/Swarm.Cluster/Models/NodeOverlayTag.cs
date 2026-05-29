namespace Swarm.Cluster.Models;

/// <summary>
/// Cluster-managed overlay tag for a <see cref="Node"/> (roadmap D6 / P2-5).
/// Pushed to the Node via the heartbeat response. The Node merges with its
/// static layer; the static value wins on key conflict.
/// </summary>
public class NodeOverlayTag
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTime UpdatedAt { get; set; }
}

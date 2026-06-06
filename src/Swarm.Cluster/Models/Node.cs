namespace Swarm.Cluster.Models;

public class Node
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public NodeStatus Status { get; set; } = NodeStatus.Offline;
    public DateTime CreatedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }

    /// <summary>
    /// Node-local static tags reported at registration (D6). JSON-serialized
    /// <c>Dictionary&lt;string, string&gt;</c>. Replaces the old
    /// <c>EnvironmentTagsJson</c> which serialized the entire IConfiguration.
    /// Overlay tags live in <see cref="NodeOverlayTag"/>.
    /// </summary>
    public string? StaticTagsJson { get; set; }

    /// <summary>
    /// P5-1: CPU core count reported at registration. Null until the Node
    /// sends a <c>NodeCapacity</c> message.
    /// </summary>
    public int? CpuCores { get; set; }

    /// <summary>
    /// P5-1: total physical/cgroup memory in bytes reported at registration.
    /// Null until the Node sends a <c>NodeCapacity</c> message.
    /// </summary>
    public long? TotalMemoryBytes { get; set; }

    /// <summary>
    /// GIN-indexed jsonb projection of this Node's <em>effective</em> tags —
    /// <c>static ∪ overlay</c>, static winning on key conflict (D6). This is a
    /// query-optimization denormalization for tag-containment routing (P3-3);
    /// <see cref="StaticTagsJson"/> + <see cref="NodeOverlayTag"/> remain the
    /// source of truth. Kept in sync by the two — and only two — tag-write
    /// sites: <c>NodeService.RegisterNodeAsync</c> (static) and
    /// <c>NodeService.UpdateOverlayTagsAsync</c> (overlay), via
    /// <see cref="Services.Tags.EffectiveTags"/>.
    /// </summary>
    public string? EffectiveTagsJson { get; set; }

    public enum NodeStatus
    {
        Online,
        Offline
    }
}

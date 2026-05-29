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

    public enum NodeStatus
    {
        Online,
        Offline
    }
}

using System;

namespace Swarm.Cluster.Models;

public class Node
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public NodeStatus Status { get; set; } = NodeStatus.Offline;
    public DateTime CreatedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public string? EnvironmentTagsJson { get; set; } // JSON serialized environment tags

    public enum NodeStatus
    {
        Online,
        Offline
    }
}

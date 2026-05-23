namespace Swarm.Cluster.Models.Dto;

public class NodeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Node.NodeStatus Status { get; set; } = Node.NodeStatus.Offline;
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

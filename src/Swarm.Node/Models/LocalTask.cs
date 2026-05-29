namespace Swarm.Node.Models;

public class LocalTask
{
    public Guid Id { get; set; }
    public Guid ClusterTaskId { get; set; }
    public string ConfigJson { get; set; } = null!;
    public string Status { get; set; } = "pending"; // pending, running, completed, failed
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

namespace Swarm.Cluster.Models;

public class TaskDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>JSON payload passed to the node when executing this task.</summary>
    public string ConfigJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TaskInstance> Instances { get; set; } = new List<TaskInstance>();
}

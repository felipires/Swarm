namespace Swarm.Cluster.Models;

public class TaskInstance
{
    public Guid Id { get; set; }
    public Guid TaskDefinitionId { get; set; }
    public Guid NodeId { get; set; }
    public TaskInstanceStatus Status { get; set; } = TaskInstanceStatus.Pending;
    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public TaskDefinition TaskDefinition { get; set; } = null!;

    public enum TaskInstanceStatus
    {
        Pending,
        Dispatched,
        Running,
        Completed,
        Failed
    }
}

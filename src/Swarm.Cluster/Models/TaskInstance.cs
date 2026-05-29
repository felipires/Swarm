namespace Swarm.Cluster.Models;

public class TaskInstance
{
    public Guid Id { get; set; }
    public Guid TaskDefinitionId { get; set; }

    /// <summary>
    /// Target Node. NULL until a shared-queue strategy (D1) sees a Node claim
    /// the message; set immediately at dispatch for per-node strategies.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>
    /// Current status. The setter is private — use <see cref="Transition"/>
    /// from application code so invalid transitions (e.g. Completed → Failed
    /// on a redelivered result message) are rejected at the model boundary.
    /// EF Core materializes via the private setter through its convention.
    /// </summary>
    public TaskInstanceStatus Status { get; private set; } = TaskInstanceStatus.Pending;

    /// <summary>
    /// JSON-encoded per-run runtime parameters (P1-6). Snapshotted at
    /// dispatch time — subsequent edits to the TaskDefinition do not affect
    /// in-flight instances.
    /// </summary>
    public string? RuntimeParamsJson { get; set; }

    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public TaskDefinition TaskDefinition { get; set; } = null!;

    public enum TaskInstanceStatus
    {
        Pending,
        Claimed,     // shared-queue claim flow (D1 / P0-3a); reserved, not yet driven
        Dispatched,
        Running,
        Completed,
        Failed,
    }

    // FSM table per roadmap §P0-2, with one relaxation: Dispatched → Completed
    // and Dispatched → Failed are allowed because the Node does not currently
    // report an intermediate Running state back to the Cluster. When P0-3a
    // wires Node-side Running reporting, the Dispatched row can be tightened
    // back to [Running, Failed].
    private static readonly IReadOnlyDictionary<TaskInstanceStatus, TaskInstanceStatus[]> Valid =
        new Dictionary<TaskInstanceStatus, TaskInstanceStatus[]>
        {
            [TaskInstanceStatus.Pending]    = [TaskInstanceStatus.Claimed, TaskInstanceStatus.Dispatched, TaskInstanceStatus.Failed],
            [TaskInstanceStatus.Claimed]    = [TaskInstanceStatus.Running, TaskInstanceStatus.Failed],
            [TaskInstanceStatus.Dispatched] = [TaskInstanceStatus.Running, TaskInstanceStatus.Completed, TaskInstanceStatus.Failed],
            [TaskInstanceStatus.Running]    = [TaskInstanceStatus.Completed, TaskInstanceStatus.Failed],
            [TaskInstanceStatus.Completed]  = [],
            [TaskInstanceStatus.Failed]     = [TaskInstanceStatus.Pending],   // retry path (P1-2)
        };

    /// <summary>
    /// Validates and applies a status transition. Throws
    /// <see cref="InvalidOperationException"/> for any transition not in the FSM.
    /// </summary>
    public void Transition(TaskInstanceStatus next)
    {
        if (!Valid[Status].Contains(next))
            throw new InvalidOperationException(
                $"Invalid transition {Status} → {next} for TaskInstance {Id}");
        Status = next;
    }
}

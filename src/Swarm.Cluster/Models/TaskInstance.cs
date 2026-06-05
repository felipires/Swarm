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
    /// Snapshot of <see cref="TaskDefinition.TaskType"/> at dispatch time
    /// (P1-4). The Node deserializes against this — later edits to the
    /// definition cannot change in-flight instance behavior.
    /// </summary>
    public string TaskType { get; set; } = "default@1";

    /// <summary>
    /// Snapshot of <see cref="TaskDefinition.ConfigJson"/> at dispatch time
    /// (P1-4). Source of truth for what payload the Node executes.
    /// </summary>
    public string ConfigJsonSnapshot { get; set; } = "{}";

    /// <summary>
    /// Snapshot of <see cref="TaskDefinition.Version"/> at dispatch time
    /// (P1-4). Useful for auditing which version of a TaskDefinition produced
    /// this instance even if the definition is edited later.
    /// </summary>
    public int TaskDefinitionVersion { get; set; } = 1;

    /// <summary>How many retry attempts have run for this instance (P1-2).</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Earliest time the retry scheduler may re-dispatch this instance (P1-2).
    /// Set when a failed instance transitions back to <c>Pending</c>.
    /// </summary>
    public DateTime? RetryAfter { get; set; }

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

    // FSM table per roadmap §P0-2, with two relaxations from the strict
    // spec: (a) Dispatched → Completed and Dispatched → Failed are allowed
    // because the Node does not currently report an intermediate Running
    // state back to the Cluster; (b) Dispatched/Running → Pending is the
    // P1-2 retry path — the result consumer routes a failure here instead
    // of through Failed when a retry is still due.
    private static readonly IReadOnlyDictionary<TaskInstanceStatus, TaskInstanceStatus[]> Valid =
        new Dictionary<TaskInstanceStatus, TaskInstanceStatus[]>
        {
            [TaskInstanceStatus.Pending]    = [TaskInstanceStatus.Claimed, TaskInstanceStatus.Dispatched, TaskInstanceStatus.Failed],
            [TaskInstanceStatus.Claimed]    = [TaskInstanceStatus.Running, TaskInstanceStatus.Failed, TaskInstanceStatus.Pending, TaskInstanceStatus.Completed],
            [TaskInstanceStatus.Dispatched] = [TaskInstanceStatus.Running, TaskInstanceStatus.Completed, TaskInstanceStatus.Failed, TaskInstanceStatus.Pending],
            // unused today, because when worker claim the task, itinstantly dispatch job execution and publish the result
            // but it can stay in case wee need implement a pause/control of tasks
            [TaskInstanceStatus.Running]    = [TaskInstanceStatus.Completed, TaskInstanceStatus.Failed, TaskInstanceStatus.Pending],
            [TaskInstanceStatus.Completed]  = [],
            [TaskInstanceStatus.Failed]     = [TaskInstanceStatus.Pending],   // manual retry path
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

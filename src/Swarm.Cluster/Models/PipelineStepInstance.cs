namespace Swarm.Cluster.Models;

/// <summary>
/// Per-run state for one <see cref="PipelineStep"/>. Created in
/// <see cref="PipelineStepInstanceStatus.Waiting"/> when the run starts,
/// transitions to <c>Dispatched</c> once <see cref="TaskInstanceId"/> is
/// bound, then <c>Completed</c> / <c>Failed</c> / <c>Skipped</c>.
///
/// Kept as a separate row from <see cref="TaskInstance"/> so future work
/// (parallel fan-out, conditional execution, dynamic retries beyond the
/// task-level retry policy) can extend without rewriting the link.
/// </summary>
public class PipelineStepInstance
{
    public Guid Id { get; set; }
    public Guid PipelineRunId { get; set; }
    public Guid PipelineStepId { get; set; }

    /// <summary>Set once the step is dispatched. Null while Waiting/Skipped.</summary>
    public Guid? TaskInstanceId { get; set; }

    public PipelineStepInstanceStatus Status { get; set; } = PipelineStepInstanceStatus.Waiting;

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum PipelineStepInstanceStatus
{
    /// <summary>Dependencies not yet all Completed.</summary>
    Waiting = 0,

    /// <summary>A <see cref="TaskInstance"/> has been dispatched for this step.</summary>
    Dispatched = 1,

    Completed = 2,
    Failed = 3,

    /// <summary>An upstream failure with <see cref="StepFailurePolicy.FailPipeline"/> halted this branch.</summary>
    Skipped = 4,
}

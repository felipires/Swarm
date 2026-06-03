namespace Swarm.Cluster.Models;

/// <summary>
/// A single execution of a <see cref="Pipeline"/> (roadmap P1-1). Captures
/// the steps' structure at trigger time in <see cref="StepsSnapshotJson"/>
/// (P1-1a) so in-flight runs are immune to later definition edits.
/// </summary>
public class PipelineRun
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }

    /// <summary>Snapshot of <see cref="Pipeline.Version"/> at trigger time.</summary>
    public int PipelineVersion { get; set; }

    /// <summary>
    /// Snapshot of all <see cref="PipelineStep"/> rows (including
    /// <c>DependsOnJson</c> and per-step overrides) at trigger time
    /// (P1-1a). Read by the step advancer instead of re-querying the
    /// possibly-edited definition.
    /// </summary>
    public string StepsSnapshotJson { get; set; } = "[]";

    public PipelineRunStatus Status { get; set; } = PipelineRunStatus.Running;

    /// <summary>
    /// JSON-encoded per-run parameters forwarded to every step's dispatch.
    /// Resolved by handlers via <c>{param:key}</c> placeholders.
    /// </summary>
    public string? RuntimeParamsJson { get; set; }

    /// <summary>Free-form human-readable explanation of what triggered this run.</summary>
    public string? TriggerReason { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum PipelineRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
}

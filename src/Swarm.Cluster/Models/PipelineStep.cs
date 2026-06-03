namespace Swarm.Cluster.Models;

/// <summary>
/// One node in a <see cref="Pipeline"/> DAG. Resolves to a single
/// <see cref="TaskInstance"/> per <see cref="PipelineRun"/> (one
/// <see cref="PipelineStepInstance"/> mediates the relationship).
///
/// Per-step dispatch overrides (<see cref="StrategyOverride"/>,
/// <see cref="TargetNodeId"/>, <see cref="TargetTagsJson"/>) let a single
/// TaskDefinition be reused with different routing per step — e.g. one
/// step pinned to a specific Node, another fanning out via tags.
/// </summary>
public class PipelineStep
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }

    /// <summary>Operator-facing label, unique within the pipeline.</summary>
    public string Name { get; set; } = null!;

    public Guid TaskDefinitionId { get; set; }

    /// <summary>
    /// JSON-encoded <c>Guid[]</c> of <see cref="PipelineStep.Id"/>s this
    /// step waits on. Empty array = root step.
    /// </summary>
    public string DependsOnJson { get; set; } = "[]";

    /// <summary>
    /// Optional override of the TaskDefinition's <c>DefaultStrategy</c>.
    /// </summary>
    public DispatchStrategy? StrategyOverride { get; set; }

    /// <summary>Required for <c>SpecificNode</c> when this step overrides strategy.</summary>
    public Guid? TargetNodeId { get; set; }

    /// <summary>JSON tag selector for <c>TaggedNodes</c> override.</summary>
    public string? TargetTagsJson { get; set; }

    public StepFailurePolicy FailurePolicy { get; set; } = StepFailurePolicy.FailPipeline;

    /// <summary>Stable sort key for UI presentation. Not used for execution.</summary>
    public int Order { get; set; }

    public Pipeline Pipeline { get; set; } = null!;
}

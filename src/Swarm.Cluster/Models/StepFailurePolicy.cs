namespace Swarm.Cluster.Models;

/// <summary>
/// What to do when a <see cref="PipelineStep"/> fails (roadmap P1-1).
/// Closed enum — add new policies here when concrete needs surface.
/// </summary>
public enum StepFailurePolicy
{
    /// <summary>
    /// Failure halts the pipeline: the failing step is marked Failed, every
    /// downstream <see cref="PipelineStepInstance"/> still in Waiting flips
    /// to Skipped, and the <see cref="PipelineRun"/> itself transitions to
    /// Failed. The default.
    /// </summary>
    FailPipeline = 0,

    /// <summary>
    /// Failure is recorded on the step but does not halt the pipeline.
    /// Downstream steps that depend on the failed step are Skipped (their
    /// dep didn't succeed). Steps in disjoint branches continue running.
    /// The run completes (status <c>Completed</c>) when no Waiting/Dispatched
    /// steps remain — even if one or more steps ended Failed.
    /// </summary>
    ContinuePipeline = 1,
}

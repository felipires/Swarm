namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// D2 boundary: <see cref="Services.TaskResultConsumerService"/> notifies
/// the advancer when a <c>TaskInstance</c> reaches a terminal status. The
/// advancer locates the owning <see cref="Models.PipelineStepInstance"/>
/// (if any), updates its state, and dispatches newly-unblocked steps.
///
/// The notify side stays tight and synchronous-looking — write to an
/// in-memory queue and return — so the result-consumer hot path doesn't
/// stall on DAG work. The processing side runs in its own scope and can
/// take as long as it needs.
///
/// Today's implementation is <see cref="InProcessStepAdvancer"/> (single
/// Cluster, in-memory Channel). When HA arrives the same interface can be
/// satisfied by a DB-polling implementation; no caller needs to change.
/// </summary>
public interface IStepAdvancer
{
    /// <summary>
    /// Schedule a DAG evaluation for the pipeline run owning
    /// <paramref name="completedTaskInstanceId"/>. No-op if the instance is
    /// not part of any pipeline (e.g. ad-hoc dispatched via the tasks API).
    /// </summary>
    ValueTask NotifyAsync(Guid completedTaskInstanceId, CancellationToken cancellationToken);
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Reactive half of the pipeline executor (roadmap P1-1). Driven by
/// <see cref="InProcessStepAdvancer"/> on every notification. Reads the
/// current per-step state for a run, applies the DAG transitions, and
/// dispatches newly-unblocked steps.
///
/// Scoped — one instance per advancement, owns its DbContext.
/// </summary>
public class PipelineRunExecutor
{
    private readonly ClusterDbContext _db;
    private readonly TaskDispatchService _dispatch;
    private readonly ILogger<PipelineRunExecutor> _logger;

    public PipelineRunExecutor(
        ClusterDbContext db,
        TaskDispatchService dispatch,
        ILogger<PipelineRunExecutor> logger)
    {
        _db = db;
        _dispatch = dispatch;
        _logger = logger;
    }

    /// <summary>
    /// Reconcile a pipeline run's step states with the latest TaskInstance
    /// results, then dispatch any steps whose dependencies have just been
    /// satisfied. Idempotent — a duplicate notification produces no new work.
    /// </summary>
    public async Task AdvanceAsync(Guid pipelineRunId, CancellationToken cancellationToken)
    {
        var run = await _db.PipelineRuns.FirstOrDefaultAsync(r => r.Id == pipelineRunId, cancellationToken);
        if (run is null)
        {
            _logger.LogWarning("AdvanceAsync called for unknown run {PipelineRunId}", pipelineRunId);
            return;
        }
        if (run.Status != PipelineRunStatus.Running)
        {
            // Terminal run; nothing to do. A late-arriving TaskInstance result
            // is still recorded on its own row, but the pipeline as a whole
            // has already been resolved.
            return;
        }

        // Pull every step instance for this run plus the TaskInstance status
        // for any that are currently Dispatched. This is one round trip.
        var stepInstances = await _db.PipelineStepInstances
            .Where(s => s.PipelineRunId == pipelineRunId)
            .ToListAsync(cancellationToken);

        var dispatchedInstanceIds = stepInstances
            .Where(s => s.TaskInstanceId is not null && s.Status == PipelineStepInstanceStatus.Dispatched)
            .Select(s => s.TaskInstanceId!.Value)
            .ToList();

        var taskResults = dispatchedInstanceIds.Count == 0
            ? new Dictionary<Guid, TaskInstance>()
            : await _db.TaskInstances
                .Where(t => dispatchedInstanceIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        var stepsSnapshot = JsonSerializer.Deserialize<List<StepSnapshot>>(run.StepsSnapshotJson)
            ?? new List<StepSnapshot>();
        var graph = BuildGraphFromSnapshot(stepsSnapshot);

        // Reconcile each dispatched step's status from the underlying TaskInstance.
        foreach (var stepInstance in stepInstances.Where(s => s.Status == PipelineStepInstanceStatus.Dispatched))
        {
            if (stepInstance.TaskInstanceId is null) continue;
            if (!taskResults.TryGetValue(stepInstance.TaskInstanceId.Value, out var task)) continue;

            switch (task.Status)
            {
                case TaskInstance.TaskInstanceStatus.Completed:
                    stepInstance.Status = PipelineStepInstanceStatus.Completed;
                    stepInstance.CompletedAt = task.CompletedAt;
                    stepInstance.ResultJson = task.ResultJson;
                    break;

                case TaskInstance.TaskInstanceStatus.Failed:
                    stepInstance.Status = PipelineStepInstanceStatus.Failed;
                    stepInstance.CompletedAt = task.CompletedAt;
                    stepInstance.ErrorMessage = task.ErrorMessage;
                    HandleFailure(stepsSnapshot, stepInstance, stepInstances, graph);
                    break;

                // Any other status (Pending, Dispatched, Running, Claimed) means the
                // task isn't yet done from the pipeline's POV — leave it Dispatched.
            }
        }

        // Persist status reconciliation BEFORE attempting new dispatches so
        // a partial failure mid-loop leaves a coherent state.
        await _db.SaveChangesAsync(cancellationToken);

        // Have we reached a terminal pipeline state?
        if (TryResolveRunStatus(stepInstances, run, out var terminalStatus))
        {
            run.Status = terminalStatus;
            run.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Pipeline run {RunId} reached terminal status {Status}", run.Id, terminalStatus);
            return;
        }

        // Dispatch newly-ready steps.
        await DispatchReadyAsync(run, stepInstances, stepsSnapshot, graph, cancellationToken);
    }

    /// <summary>
    /// Entry point for "start a brand-new run" — equivalent to advancing
    /// from the initial empty state. Called by <see cref="PipelineService.StartRunAsync"/>
    /// after the run row + waiting step instances are persisted.
    /// </summary>
    public async Task DispatchRootsAsync(Guid pipelineRunId, CancellationToken cancellationToken)
        => await AdvanceAsync(pipelineRunId, cancellationToken);

    private async Task DispatchReadyAsync(
        PipelineRun run,
        List<PipelineStepInstance> stepInstances,
        List<StepSnapshot> stepsSnapshot,
        PipelineGraph graph,
        CancellationToken cancellationToken)
    {
        var stateByStepId = stepInstances.ToDictionary(s => s.PipelineStepId, s => s.Status);
        var ready = graph.ResolveNextReady(stateByStepId);
        if (ready.Count == 0) return;

        var snapshotByStepId = stepsSnapshot.ToDictionary(s => s.StepId);

        foreach (var stepId in ready)
        {
            var stepInstance = stepInstances.First(s => s.PipelineStepId == stepId);
            var snapshot = snapshotByStepId[stepId];

            TaskInstance dispatched;
            try
            {
                var strategy = snapshot.Strategy;
                var targetTags = string.IsNullOrEmpty(snapshot.TargetTagsJson)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.TargetTagsJson);

                var effectiveParams = BuildEffectiveParams(
                    run.RuntimeParamsJson,
                    snapshot,
                    stepsSnapshot,
                    stepInstances,
                    _logger);

                dispatched = await _dispatch.DispatchAsync(
                    snapshot.TaskDefinitionId,
                    nodeId: snapshot.TargetNodeId,
                    strategy: strategy,
                    targetTags: targetTags,
                    runtimeParamsJson: effectiveParams);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Dispatch of step {StepId} ({StepName}) failed; marking instance Failed",
                    stepId, snapshot.Name);
                stepInstance.Status = PipelineStepInstanceStatus.Failed;
                stepInstance.ErrorMessage = $"DISPATCH_FAILED: {ex.Message}";
                stepInstance.CompletedAt = DateTime.UtcNow;
                HandleFailure(stepsSnapshot, stepInstance, stepInstances, graph);
                continue;
            }

            stepInstance.TaskInstanceId = dispatched.Id;
            stepInstance.Status = PipelineStepInstanceStatus.Dispatched;
            stepInstance.DispatchedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "Dispatched step {StepName} ({StepId}) of run {RunId} as task {TaskInstanceId}",
                snapshot.Name, stepId, run.Id, dispatched.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // After dispatching, the run is still Running until results come back.
        // If every step ended up Failed/Skipped synchronously (dispatch failure
        // path with FailPipeline), resolve the run terminal-state now.
        if (TryResolveRunStatus(stepInstances, run, out var terminalStatus))
        {
            run.Status = terminalStatus;
            run.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// On a step failure, walk descendants and mark Waiting ones as Skipped
    /// when the failure policy is <see cref="StepFailurePolicy.FailPipeline"/>.
    /// For <c>ContinuePipeline</c>, only direct dependents that need this
    /// step's success get skipped.
    /// </summary>
    private void HandleFailure(
        List<StepSnapshot> stepsSnapshot,
        PipelineStepInstance failed,
        List<PipelineStepInstance> stepInstances,
        PipelineGraph graph)
    {
        var snapshotByStepId = stepsSnapshot.ToDictionary(s => s.StepId);
        var policy = snapshotByStepId.TryGetValue(failed.PipelineStepId, out var snap)
            ? snap.FailurePolicy
            : StepFailurePolicy.FailPipeline;

        // In either policy, every transitive descendant whose chain runs
        // through the failed step is blocked (its dep didn't succeed).
        // ContinuePipeline lets disjoint branches keep running — that
        // happens implicitly because we only skip descendants of the
        // failed step, not unrelated Waiting steps.
        foreach (var descendantId in graph.Descendants(failed.PipelineStepId))
        {
            var instance = stepInstances.FirstOrDefault(s => s.PipelineStepId == descendantId);
            if (instance is null) continue;
            if (instance.Status == PipelineStepInstanceStatus.Waiting)
            {
                instance.Status = PipelineStepInstanceStatus.Skipped;
                instance.CompletedAt = DateTime.UtcNow;
                instance.ErrorMessage = policy == StepFailurePolicy.FailPipeline
                    ? "SKIPPED_UPSTREAM_FAILED"
                    : "SKIPPED_DEP_FAILED";
            }
        }
    }

    /// <summary>
    /// Decide whether the pipeline as a whole has reached a terminal state.
    /// Returns true and the status to apply; false if the run is still
    /// progressing.
    /// </summary>
    private static bool TryResolveRunStatus(
        List<PipelineStepInstance> stepInstances,
        PipelineRun run,
        out PipelineRunStatus terminal)
    {
        terminal = PipelineRunStatus.Running;

        // If anything is still Waiting or Dispatched, the run isn't done.
        if (stepInstances.Any(s => s.Status == PipelineStepInstanceStatus.Waiting
                                   || s.Status == PipelineStepInstanceStatus.Dispatched))
        {
            return false;
        }

        // Otherwise every step is in {Completed, Failed, Skipped}.
        var anyFailed = stepInstances.Any(s => s.Status == PipelineStepInstanceStatus.Failed);
        terminal = anyFailed ? PipelineRunStatus.Failed : PipelineRunStatus.Completed;
        if (anyFailed)
        {
            var firstFailure = stepInstances.First(s => s.Status == PipelineStepInstanceStatus.Failed);
            run.ErrorMessage = firstFailure.ErrorMessage;
        }
        return true;
    }

    /// <summary>
    /// Merge the run-level runtime params with any output mappings declared
    /// on <paramref name="snapshot"/>. Step-level mapped values win on
    /// collision with run-level params.
    /// </summary>
    /// <summary>
    /// Resolves the runtime params a step is dispatched with. Precedence, lowest
    /// to highest: run-level params, per-step static params (P1-9), then output
    /// mappings (live upstream values). Pure and logger-optional so it can be
    /// unit-tested directly.
    /// </summary>
    internal static string? BuildEffectiveParams(
        string? runRuntimeParamsJson,
        StepSnapshot snapshot,
        List<StepSnapshot> allSnapshots,
        List<PipelineStepInstance> stepInstances,
        ILogger? logger = null)
    {
        var hasStepParams = !string.IsNullOrWhiteSpace(snapshot.RuntimeParamsJson);
        var hasMappings = snapshot.OutputMappings is { Count: > 0 };

        // Fast path: nothing step-specific to merge.
        if (!hasStepParams && !hasMappings)
            return runRuntimeParamsJson;

        // Precedence (lowest → highest): run params, then per-step static
        // params (P1-9), then output mappings (live upstream values win). This
        // is what lets two steps sharing a TaskDefinition be parameterized
        // differently.
        var merged = string.IsNullOrEmpty(runRuntimeParamsJson)
            ? new JsonObject()
            : JsonNode.Parse(runRuntimeParamsJson)?.AsObject() ?? new JsonObject();

        if (hasStepParams)
        {
            var stepParams = JsonNode.Parse(snapshot.RuntimeParamsJson!)?.AsObject();
            if (stepParams is not null)
            {
                foreach (var kv in stepParams)
                    merged[kv.Key] = kv.Value?.DeepClone();
            }
        }

        if (!hasMappings)
            return merged.ToJsonString();

        // Build lookup: step name → completed step instance (with ResultJson).
        var completedByName = allSnapshots
            .Join(stepInstances,
                s => s.StepId,
                i => i.PipelineStepId,
                (s, i) => (s.Name, Instance: i))
            .Where(x => x.Instance.Status == PipelineStepInstanceStatus.Completed)
            .ToDictionary(x => x.Name, x => x.Instance, StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in snapshot.OutputMappings!)
        {
            if (!completedByName.TryGetValue(mapping.FromStep, out var upstream))
            {
                logger?.LogDebug(
                    "Output mapping fromStep '{FromStep}' not found or not completed; skipping",
                    mapping.FromStep);
                continue;
            }

            if (string.IsNullOrEmpty(upstream.ResultJson)) continue;

            JsonElement resultDoc;
            try { resultDoc = JsonSerializer.Deserialize<JsonElement>(upstream.ResultJson); }
            catch (JsonException) { continue; }

            var extracted = OutputMappingPathExtractor.Extract(resultDoc, mapping.FromPath);
            if (extracted is null)
            {
                logger?.LogDebug(
                    "Output mapping path '{FromPath}' not found in step '{FromStep}' result; skipping",
                    mapping.FromPath, mapping.FromStep);
                continue;
            }

            merged[mapping.ToParam] = JsonValue.Create(extracted);
        }

        return merged.ToJsonString();
    }

    private static PipelineGraph BuildGraphFromSnapshot(List<StepSnapshot> snapshots)
    {
        // The graph wants PipelineStep entities — build minimal stand-ins.
        var steps = snapshots.Select(s => new PipelineStep
        {
            Id = s.StepId,
            PipelineId = Guid.Empty,
            Name = s.Name,
            TaskDefinitionId = s.TaskDefinitionId,
            DependsOnJson = PipelineGraph.DependencyDecoder.Encode(s.DependsOn),
            StrategyOverride = s.Strategy,
            TargetNodeId = s.TargetNodeId,
            TargetTagsJson = s.TargetTagsJson,
            FailurePolicy = s.FailurePolicy,
            OutputMappingsJson = s.OutputMappings is { Count: > 0 }
                ? JsonSerializer.Serialize(s.OutputMappings)
                : null,
        }).ToList();
        return PipelineGraph.Build(steps);
    }

    /// <summary>
    /// Stable wire shape persisted in <see cref="PipelineRun.StepsSnapshotJson"/>.
    /// Decoupled from the EF entity so future <see cref="PipelineStep"/>
    /// schema changes don't break replay of historical runs.
    /// </summary>
    public sealed record StepSnapshot(
        Guid StepId,
        string Name,
        Guid TaskDefinitionId,
        List<Guid> DependsOn,
        DispatchStrategy? Strategy,
        Guid? TargetNodeId,
        string? TargetTagsJson,
        StepFailurePolicy FailurePolicy,
        List<OutputMapping>? OutputMappings = null,
        string? RuntimeParamsJson = null);
}

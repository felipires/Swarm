using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Operator-facing pipeline lifecycle (roadmap P1-1): create/edit
/// definitions, start runs, query run state. Reactive step-by-step
/// advancement lives in <see cref="PipelineRunExecutor"/>.
/// </summary>
public class PipelineService
{
    private readonly ClusterDbContext _db;
    private readonly PipelineRunExecutor _executor;
    private readonly Swarm.Cluster.Services.EntityVersionService _versions;
    private readonly ILogger<PipelineService> _logger;

    public PipelineService(
        ClusterDbContext db,
        PipelineRunExecutor executor,
        Swarm.Cluster.Services.EntityVersionService versions,
        ILogger<PipelineService> logger)
    {
        _db = db;
        _executor = executor;
        _versions = versions;
        _logger = logger;
    }

    public sealed record StepDefinition(
        string Name,
        Guid TaskDefinitionId,
        IReadOnlyList<string> DependsOnByName,
        DispatchStrategy? StrategyOverride = null,
        Guid? TargetNodeId = null,
        Dictionary<string, string>? TargetTags = null,
        StepFailurePolicy FailurePolicy = StepFailurePolicy.FailPipeline,
        int Order = 0,
        List<OutputMapping>? OutputMappings = null,
        string? RuntimeParamsJson = null);

    /// <summary>Create-request-shaped snapshot persisted per pipeline version (P1-10).</summary>
    public sealed record PipelineSnapshot(
        string Name,
        string? Description,
        List<StepDefinition> Steps);

    private static PipelineSnapshot SnapshotOf(string name, string? description, IReadOnlyList<StepDefinition> steps)
        => new(name, description, steps.ToList());

    /// <summary>
    /// Create a new pipeline. Step IDs are generated server-side; the
    /// caller refers to steps by name in <c>DependsOnByName</c> and we
    /// resolve to GUIDs here.
    /// </summary>
    /// <summary>
    /// Validate referenced TaskDefinitions, resolve step names → fresh GUIDs for
    /// the given pipeline, build the step entities, and validate the graph
    /// (cycles, dangling deps, output-mapping ancestor constraint). Shared by
    /// create and update.
    /// </summary>
    private async Task<List<PipelineStep>> BuildAndValidateStepsAsync(
        Guid pipelineId, IReadOnlyList<StepDefinition> steps, CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
            throw new PipelineGraphException("EMPTY_PIPELINE", "Pipeline must have at least one step");

        var taskDefIds = steps.Select(s => s.TaskDefinitionId).Distinct().ToList();
        var found = await _db.TaskDefinitions
            .Where(t => taskDefIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var missing = taskDefIds.Except(found).ToList();
        if (missing.Count > 0)
            throw new PipelineGraphException("UNKNOWN_TASK_DEFINITION",
                $"TaskDefinition(s) not found: {string.Join(", ", missing)}");

        var nameToId = steps.ToDictionary(s => s.Name, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        if (nameToId.Count != steps.Count)
            throw new PipelineGraphException("DUPLICATE_STEP_NAME", "Step names must be unique within a pipeline");

        var stepEntities = new List<PipelineStep>(steps.Count);
        foreach (var s in steps)
        {
            var dependsOn = new List<Guid>(s.DependsOnByName.Count);
            foreach (var depName in s.DependsOnByName)
            {
                if (!nameToId.TryGetValue(depName, out var depId))
                    throw new PipelineGraphException("DANGLING_DEPENDENCY",
                        $"Step '{s.Name}' depends on unknown step '{depName}'");
                dependsOn.Add(depId);
            }

            stepEntities.Add(new PipelineStep
            {
                Id = nameToId[s.Name],
                PipelineId = pipelineId,
                Name = s.Name,
                TaskDefinitionId = s.TaskDefinitionId,
                DependsOnJson = PipelineGraph.DependencyDecoder.Encode(dependsOn),
                StrategyOverride = s.StrategyOverride,
                TargetNodeId = s.TargetNodeId,
                TargetTagsJson = s.TargetTags is { Count: > 0 } ? JsonSerializer.Serialize(s.TargetTags) : null,
                FailurePolicy = s.FailurePolicy,
                Order = s.Order,
                OutputMappingsJson = s.OutputMappings is { Count: > 0 }
                    ? JsonSerializer.Serialize(s.OutputMappings)
                    : null,
                RuntimeParamsJson = string.IsNullOrWhiteSpace(s.RuntimeParamsJson)
                    ? null
                    : s.RuntimeParamsJson,
            });
        }

        var graph = PipelineGraph.Build(stepEntities);

        foreach (var s in steps)
        {
            if (s.OutputMappings is not { Count: > 0 }) continue;
            var stepId = nameToId[s.Name];
            var ancestors = graph.Ancestors(stepId);
            foreach (var mapping in s.OutputMappings)
            {
                if (!nameToId.TryGetValue(mapping.FromStep, out var fromId))
                    throw new PipelineGraphException("STEP_OUTPUT_NOT_ANCESTOR",
                        $"Step '{s.Name}' output mapping references unknown step '{mapping.FromStep}'");
                if (!ancestors.Contains(fromId))
                    throw new PipelineGraphException("STEP_OUTPUT_NOT_ANCESTOR",
                        $"Step '{s.Name}' output mapping references '{mapping.FromStep}' which is not an ancestor");
            }
        }

        return stepEntities;
    }

    public async Task<Pipeline> CreateAsync(string name, string? description, IReadOnlyList<StepDefinition> steps, CancellationToken cancellationToken)
    {
        var pipelineId = Guid.NewGuid();
        var stepEntities = await BuildAndValidateStepsAsync(pipelineId, steps, cancellationToken);

        var pipeline = new Pipeline
        {
            Id = pipelineId,
            Name = name,
            Description = description,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Steps = stepEntities,
        };
        _db.Pipelines.Add(pipeline);
        _versions.Record(VersionedEntityType.Pipeline, pipeline.Id, pipeline.Version,
            SnapshotOf(name, description, steps));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created pipeline {PipelineId} '{Name}' with {StepCount} step(s)",
            pipeline.Id, pipeline.Name, stepEntities.Count);
        return pipeline;
    }

    /// <summary>Thrown when an optimistic-concurrency check fails (P1-10).</summary>
    public sealed class VersionConflictException(int current, int expected)
        : Exception($"Pipeline is at version {current}, not {expected}; reload before editing")
    {
        public int Current { get; } = current;
        public int Expected { get; } = expected;
    }

    /// <summary>
    /// Replace a pipeline's definition as a new version (P1-10). Validates the
    /// new graph, swaps the step set (delete + recreate — safe: step instances
    /// have no FK to PipelineStep and runs read from their own snapshot), bumps
    /// Version, and records a history row. In-flight runs are insulated.
    /// </summary>
    public async Task<Pipeline> UpdateAsync(
        Guid id, string name, string? description, IReadOnlyList<StepDefinition> steps,
        int? expectedVersion, CancellationToken cancellationToken)
    {
        var pipeline = await _db.Pipelines
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Pipeline {id} not found");

        if (expectedVersion is { } expected && expected != pipeline.Version)
            throw new VersionConflictException(pipeline.Version, expected);

        var stepEntities = await BuildAndValidateStepsAsync(pipeline.Id, steps, cancellationToken);

        // Hard-replace the step set: delete the tracked old rows, add the fresh
        // ones (their PipelineId is already set). Avoid reassigning the tracked
        // navigation collection, which conflicts with the manual RemoveRange.
        _db.PipelineSteps.RemoveRange(pipeline.Steps.ToList());
        await _db.PipelineSteps.AddRangeAsync(stepEntities, cancellationToken);
        pipeline.Name = name;
        pipeline.Description = description;
        pipeline.Version += 1;
        pipeline.UpdatedAt = DateTime.UtcNow;

        _versions.Record(VersionedEntityType.Pipeline, pipeline.Id, pipeline.Version,
            SnapshotOf(name, description, steps));
        await _db.SaveChangesAsync(cancellationToken);

        // Reflect the new step set on the returned entity (the tracked navigation
        // still held the now-deleted rows until this point).
        pipeline.Steps = stepEntities;

        _logger.LogInformation("Updated pipeline {PipelineId} → v{Version}", pipeline.Id, pipeline.Version);
        return pipeline;
    }

    public async Task<Pipeline?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Pipelines.Include(p => p.Steps).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(List<Pipeline> Items, int Total)> ListAsync(
        int skip, int take, bool includeDeleted, CancellationToken cancellationToken)
    {
        // includeDeleted bypasses the global soft-delete filter so operators can
        // see and restore deleted pipelines.
        var baseSet = includeDeleted ? _db.Pipelines.IgnoreQueryFilters() : _db.Pipelines;
        var query = baseSet.OrderByDescending(p => p.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(skip).Take(take).Include(p => p.Steps).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // FirstOrDefault (not Find) so the soft-delete filter applies — an
        // already-deleted pipeline reads as not found.
        var pipeline = await _db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pipeline is null)
            throw new InvalidOperationException($"Pipeline {id} not found");
        // Soft delete (P1-10): hidden by the global query filter; runs + history survive.
        pipeline.IsDeleted = true;
        pipeline.DeletedAt = DateTime.UtcNow;

        // Disable any schedules so the sweeper doesn't keep firing a hidden
        // pipeline (StartRunAsync would throw not-found every tick).
        var schedules = await _db.Schedules
            .Where(s => s.PipelineId == id && s.Enabled)
            .ToListAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            schedule.Enabled = false;
            schedule.NextFireAt = null;
            schedule.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (schedules.Count > 0)
            _logger.LogInformation("Disabled {Count} schedule(s) on pipeline {Id} delete", schedules.Count, id);
    }

    public async Task UndeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Must ignore the filter to find a soft-deleted row.
        var pipeline = await _db.Pipelines.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pipeline is null)
            throw new InvalidOperationException($"Pipeline {id} not found");
        pipeline.IsDeleted = false;
        pipeline.DeletedAt = null;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Start a new run. Snapshots the current step set into
    /// <see cref="PipelineRun.StepsSnapshotJson"/>, creates one
    /// <see cref="PipelineStepInstance"/> per step in <c>Waiting</c>, then
    /// hands off to <see cref="PipelineRunExecutor"/> which dispatches the
    /// root steps in the same scope.
    /// </summary>
    public async Task<PipelineRun> StartRunAsync(
        Guid pipelineId,
        string? runtimeParamsJson,
        string? triggerReason,
        CancellationToken cancellationToken)
    {
        var pipeline = await _db.Pipelines
            .Include(p => p.Steps)
            .FirstOrDefaultAsync(p => p.Id == pipelineId, cancellationToken);
        if (pipeline is null)
            throw new InvalidOperationException($"Pipeline {pipelineId} not found");

        // Validate the current shape one more time — definition could have
        // been edited into a cycle since last validated.
        _ = PipelineGraph.Build(pipeline.Steps.ToList());

        var snapshots = pipeline.Steps.Select(s => new PipelineRunExecutor.StepSnapshot(
            StepId: s.Id,
            Name: s.Name,
            TaskDefinitionId: s.TaskDefinitionId,
            DependsOn: PipelineGraph.DependencyDecoder.Decode(s.DependsOnJson).ToList(),
            Strategy: s.StrategyOverride,
            TargetNodeId: s.TargetNodeId,
            TargetTagsJson: s.TargetTagsJson,
            FailurePolicy: s.FailurePolicy,
            OutputMappings: string.IsNullOrEmpty(s.OutputMappingsJson)
                ? null
                : JsonSerializer.Deserialize<List<OutputMapping>>(s.OutputMappingsJson),
            RuntimeParamsJson: s.RuntimeParamsJson)).ToList();

        var run = new PipelineRun
        {
            Id = Guid.NewGuid(),
            PipelineId = pipeline.Id,
            PipelineVersion = pipeline.Version,
            StepsSnapshotJson = JsonSerializer.Serialize(snapshots),
            Status = PipelineRunStatus.Running,
            RuntimeParamsJson = runtimeParamsJson,
            TriggerReason = triggerReason,
            StartedAt = DateTime.UtcNow,
        };

        var stepInstances = pipeline.Steps.Select(s => new PipelineStepInstance
        {
            Id = Guid.NewGuid(),
            PipelineRunId = run.Id,
            PipelineStepId = s.Id,
            Status = PipelineStepInstanceStatus.Waiting,
            CreatedAt = DateTime.UtcNow,
        }).ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.PipelineRuns.Add(run);
        _db.PipelineStepInstances.AddRange(stepInstances);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Started pipeline run {RunId} for pipeline {PipelineId} v{Version} ({Steps} steps)",
            run.Id, pipeline.Id, pipeline.Version, stepInstances.Count);

        // Dispatch root steps (no deps). Subsequent advances come via
        // IStepAdvancer.NotifyAsync from the result consumer.
        await _executor.DispatchRootsAsync(run.Id, cancellationToken);

        return run;
    }

    /// <summary>Thrown when a run can't be retried (not failed / nothing to retry).</summary>
    public sealed class RunNotRetryableException(string message) : Exception(message);

    /// <summary>
    /// Resume a failed run as a new run that re-executes only the failed/skipped
    /// frontier (P1-9 follow-up). Reuses the source run's snapshot, version, and
    /// runtime params; seeds previously-Completed steps as Completed (carrying
    /// their ResultJson so output mappings still resolve) and everything else as
    /// Waiting. The existing executor then dispatches the ready frontier.
    /// </summary>
    public async Task<PipelineRun> RetryFailedAsync(Guid sourceRunId, CancellationToken cancellationToken)
    {
        var source = await _db.PipelineRuns.FirstOrDefaultAsync(r => r.Id == sourceRunId, cancellationToken)
            ?? throw new InvalidOperationException($"PipelineRun {sourceRunId} not found");

        if (source.Status != PipelineRunStatus.Failed)
            throw new RunNotRetryableException("Only failed runs can be retried");

        var sourceSteps = await _db.PipelineStepInstances
            .Where(s => s.PipelineRunId == sourceRunId)
            .ToListAsync(cancellationToken);

        if (!sourceSteps.Any(s => s.Status is PipelineStepInstanceStatus.Failed or PipelineStepInstanceStatus.Skipped))
            throw new RunNotRetryableException("Run has no failed or skipped steps to retry");

        var run = new PipelineRun
        {
            Id = Guid.NewGuid(),
            PipelineId = source.PipelineId,
            PipelineVersion = source.PipelineVersion,
            StepsSnapshotJson = source.StepsSnapshotJson,  // rerun the same definition snapshot
            Status = PipelineRunStatus.Running,
            RuntimeParamsJson = source.RuntimeParamsJson,
            TriggerReason = $"retry:{sourceRunId}",
            StartedAt = DateTime.UtcNow,
        };

        var now = DateTime.UtcNow;
        var stepInstances = sourceSteps.Select(s =>
        {
            var carry = s.Status == PipelineStepInstanceStatus.Completed;
            return new PipelineStepInstance
            {
                Id = Guid.NewGuid(),
                PipelineRunId = run.Id,
                PipelineStepId = s.PipelineStepId,
                // Completed steps are reused (not re-executed); everything else reruns.
                Status = carry ? PipelineStepInstanceStatus.Completed : PipelineStepInstanceStatus.Waiting,
                ResultJson = carry ? s.ResultJson : null,
                CompletedAt = carry ? s.CompletedAt : null,
                CreatedAt = now,
            };
        }).ToList();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        _db.PipelineRuns.Add(run);
        _db.PipelineStepInstances.AddRange(stepInstances);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Retrying failed run {SourceRunId} as new run {RunId} ({Reused} reused, {Rerun} to rerun)",
            sourceRunId, run.Id,
            stepInstances.Count(s => s.Status == PipelineStepInstanceStatus.Completed),
            stepInstances.Count(s => s.Status == PipelineStepInstanceStatus.Waiting));

        await _executor.DispatchRootsAsync(run.Id, cancellationToken);
        return run;
    }

    public async Task<PipelineRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
        => await _db.PipelineRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task<List<PipelineStepInstance>> GetRunStepsAsync(Guid runId, CancellationToken cancellationToken)
        => await _db.PipelineStepInstances
            .Where(s => s.PipelineRunId == runId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<List<PipelineRun>> GetRunsByPipelineId(Guid pipelineId, CancellationToken cancellationToken)
    {
        return await _db.PipelineRuns.Where(x => x.PipelineId == pipelineId)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(cancellationToken);
    }
}

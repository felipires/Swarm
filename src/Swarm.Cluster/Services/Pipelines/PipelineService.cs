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
    private readonly ILogger<PipelineService> _logger;

    public PipelineService(ClusterDbContext db, PipelineRunExecutor executor, ILogger<PipelineService> logger)
    {
        _db = db;
        _executor = executor;
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
        int Order = 0);

    /// <summary>
    /// Create a new pipeline. Step IDs are generated server-side; the
    /// caller refers to steps by name in <c>DependsOnByName</c> and we
    /// resolve to GUIDs here.
    /// </summary>
    public async Task<Pipeline> CreateAsync(string name, string? description, IReadOnlyList<StepDefinition> steps, CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
            throw new PipelineGraphException("EMPTY_PIPELINE", "Pipeline must have at least one step");

        // Validate all referenced TaskDefinitions exist before we start
        // assigning GUIDs.
        var taskDefIds = steps.Select(s => s.TaskDefinitionId).Distinct().ToList();
        var found = await _db.TaskDefinitions
            .Where(t => taskDefIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var missing = taskDefIds.Except(found).ToList();
        if (missing.Count > 0)
            throw new PipelineGraphException("UNKNOWN_TASK_DEFINITION",
                $"TaskDefinition(s) not found: {string.Join(", ", missing)}");

        // Generate IDs + resolve name → id, then validate via the graph.
        var pipelineId = Guid.NewGuid();
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
            });
        }

        // Validate the assembled graph (cycles, self-loops, etc.) before
        // committing anything.
        _ = PipelineGraph.Build(stepEntities);

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
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created pipeline {PipelineId} '{Name}' with {StepCount} step(s)",
            pipeline.Id, pipeline.Name, stepEntities.Count);
        return pipeline;
    }

    public async Task<Pipeline?> GetAsync(Guid id, CancellationToken cancellationToken)
        => await _db.Pipelines.Include(p => p.Steps).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<(List<Pipeline> Items, int Total)> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var query = _db.Pipelines.OrderByDescending(p => p.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(skip).Take(take).Include(p => p.Steps).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var pipeline = await _db.Pipelines.FindAsync([id], cancellationToken);
        if (pipeline is null)
            throw new InvalidOperationException($"Pipeline {id} not found");
        _db.Pipelines.Remove(pipeline);
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
            FailurePolicy: s.FailurePolicy)).ToList();

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

    public async Task<PipelineRun?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
        => await _db.PipelineRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task<List<PipelineStepInstance>> GetRunStepsAsync(Guid runId, CancellationToken cancellationToken)
        => await _db.PipelineStepInstances
            .Where(s => s.PipelineRunId == runId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
}

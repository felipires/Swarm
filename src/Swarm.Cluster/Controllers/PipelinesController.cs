using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services.Pipelines;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PipelinesController : ControllerBase
{
    private readonly PipelineService _service;
    private readonly Swarm.Cluster.Services.EntityVersionService _versions;
    private readonly ILogger<PipelinesController> _logger;

    public PipelinesController(
        PipelineService service,
        Swarm.Cluster.Services.EntityVersionService versions,
        ILogger<PipelinesController> logger)
    {
        _service = service;
        _versions = versions;
        _logger = logger;
    }

    private static List<PipelineService.StepDefinition> ToStepDefs(List<CreatePipelineStep> steps)
        => steps.Select(s => new PipelineService.StepDefinition(
            Name: s.Name,
            TaskDefinitionId: s.TaskDefinitionId,
            DependsOnByName: s.DependsOn ?? new List<string>(),
            StrategyOverride: s.Strategy,
            TargetNodeId: s.TargetNodeId,
            TargetTags: s.TargetTags,
            FailurePolicy: s.FailurePolicy,
            Order: s.Order,
            OutputMappings: s.OutputMappings,
            RuntimeParamsJson: s.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
                ? s.RuntimeParams.Value.GetRawText()
                : null)).ToList();

    [HttpGet]
    public async Task<ActionResult<PagedResult<PipelineResponse>>> List(
        [FromQuery] PageRequest page,
        [FromQuery] bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var (items, total) = await _service.ListAsync(
            page.Skip, page.NormalizedPageSize, includeDeleted, cancellationToken);
        var responses = items.Select(PipelineResponse.From).ToList();
        return Ok(new PagedResult<PipelineResponse>(responses, total, page.NormalizedPage, page.NormalizedPageSize));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PipelineResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var pipeline = await _service.GetAsync(id, cancellationToken);
        if (pipeline is null)
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        return Ok(PipelineResponse.From(pipeline));
    }

    [HttpPost]
    public async Task<ActionResult<PipelineResponse>> Create([FromBody] CreatePipelineRequest req, CancellationToken cancellationToken)
    {
        try
        {
            var pipeline = await _service.CreateAsync(req.Name, req.Description, ToStepDefs(req.Steps), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = pipeline.Id }, PipelineResponse.From(pipeline));
        }
        catch (PipelineGraphException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PipelineResponse>> Update(
        Guid id, [FromBody] UpdatePipelineRequest req, CancellationToken cancellationToken)
    {
        try
        {
            var pipeline = await _service.UpdateAsync(
                id, req.Name, req.Description, ToStepDefs(req.Steps), req.ExpectedVersion, cancellationToken);
            return Ok(PipelineResponse.From(pipeline));
        }
        catch (PipelineService.VersionConflictException ex)
        {
            return Conflict(new ApiError("VERSION_CONFLICT", ex.Message));
        }
        catch (PipelineGraphException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    // --- Version history (P1-10) ---

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<EntityVersionResponse>>> ListVersions(Guid id, CancellationToken ct)
    {
        if (await _service.GetAsync(id, ct) is null)
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        var rows = await _versions.ListAsync(VersionedEntityType.Pipeline, id, ct);
        return Ok(rows.Select(EntityVersionResponse.Meta).ToList());
    }

    [HttpGet("{id}/versions/{version:int}")]
    public async Task<ActionResult<EntityVersionResponse>> GetVersion(Guid id, int version, CancellationToken ct)
    {
        var row = await _versions.GetAsync(VersionedEntityType.Pipeline, id, version, ct);
        if (row == null) return NotFound(new ApiError("VERSION_NOT_FOUND", $"Pipeline {id} has no version {version}"));
        return Ok(EntityVersionResponse.Full(row));
    }

    [HttpPost("{id}/versions/{version:int}/restore")]
    public async Task<ActionResult<PipelineResponse>> RestoreVersion(Guid id, int version, CancellationToken ct)
    {
        var row = await _versions.GetAsync(VersionedEntityType.Pipeline, id, version, ct);
        if (row == null) return NotFound(new ApiError("VERSION_NOT_FOUND", $"Pipeline {id} has no version {version}"));

        var snap = Swarm.Cluster.Services.EntityVersionService.Deserialize<PipelineService.PipelineSnapshot>(row.SnapshotJson);
        try
        {
            var pipeline = await _service.UpdateAsync(
                id, snap.Name, snap.Description, snap.Steps, expectedVersion: null, ct);
            _logger.LogInformation("Restored pipeline {Id} version {From} as v{To}", id, version, pipeline.Version);
            return Ok(PipelineResponse.From(pipeline));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    [HttpPost("{id}/undelete")]
    public async Task<ActionResult> Undelete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _service.UndeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    [HttpGet("{id}/runs")]
    public async Task<IActionResult> GetRunsById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var runs = await _service.GetRunsByPipelineId(id, cancellationToken);
            return Ok(new PagedResult<PipelineRunResponse>(runs.Select(PipelineRunResponse.From).ToList(), runs.Count, 0, runs.Count));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    [HttpPost("{id}/run")]
    public async Task<ActionResult<PipelineRunResponse>> StartRun(
        Guid id,
        [FromBody] StartPipelineRunRequest? body,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtimeParamsJson = body?.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
                ? body.RuntimeParams.Value.GetRawText()
                : null;
            var run = await _service.StartRunAsync(id, runtimeParamsJson, body?.TriggerReason, cancellationToken);
            return Accepted(PipelineRunResponse.From(run));
        }
        catch (PipelineGraphException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
        }
    }

    [HttpGet("runs/{runId}")]
    public async Task<ActionResult<PipelineRunResponse>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var run = await _service.GetRunAsync(runId, cancellationToken);
        if (run is null)
            return NotFound(new ApiError("RUN_NOT_FOUND", $"PipelineRun {runId} not found"));
        return Ok(PipelineRunResponse.From(run));
    }

    [HttpGet("runs/{runId}/steps")]
    public async Task<ActionResult<List<PipelineStepInstanceResponse>>> GetRunSteps(Guid runId, CancellationToken cancellationToken)
    {
        var steps = await _service.GetRunStepsAsync(runId, cancellationToken);
        return Ok(steps.Select(PipelineStepInstanceResponse.From).ToList());
    }

    /// <summary>Resume a failed run, re-executing only the failed/skipped steps (P1-9 follow-up).</summary>
    [HttpPost("runs/{runId}/retry-failed")]
    public async Task<ActionResult<PipelineRunResponse>> RetryFailed(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _service.RetryFailedAsync(runId, cancellationToken);
            return Accepted(PipelineRunResponse.From(run));
        }
        catch (PipelineService.RunNotRetryableException ex)
        {
            return BadRequest(new ApiError("RUN_NOT_RETRYABLE", ex.Message));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("RUN_NOT_FOUND", $"PipelineRun {runId} not found"));
        }
    }

    // --- P1-3 schedules under the pipeline they belong to ---

    [HttpPost("{id}/schedules")]
    public async Task<ActionResult<ScheduleResponse>> CreateSchedule(
        Guid id,
        [FromBody] CreateScheduleRequest req,
        [FromServices] ScheduleService schedules,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtimeParamsJson = req.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
                ? req.RuntimeParams.Value.GetRawText()
                : null;
            var schedule = await schedules.CreateAsync(
                id,
                req.CronExpression,
                req.TimeZoneId ?? "UTC",
                req.Enabled ?? true,
                runtimeParamsJson,
                cancellationToken);
            return CreatedAtAction(nameof(GetSchedule), new { scheduleId = schedule.Id }, ScheduleResponse.From(schedule));
        }
        catch (CronScheduleException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("PIPELINE_NOT_FOUND", $"Pipeline {id} not found"));
        }
    }

    [HttpGet("{id}/schedules")]
    public async Task<ActionResult<List<ScheduleResponse>>> ListSchedules(
        Guid id,
        [FromServices] ScheduleService schedules,
        CancellationToken cancellationToken)
    {
        var list = await schedules.ListForPipelineAsync(id, cancellationToken);
        return Ok(list.Select(ScheduleResponse.From).ToList());
    }

    [HttpGet("schedules/{scheduleId}")]
    public async Task<ActionResult<ScheduleResponse>> GetSchedule(
        Guid scheduleId,
        [FromServices] ScheduleService schedules,
        CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetAsync(scheduleId, cancellationToken);
        if (schedule is null)
            return NotFound(new ApiError("SCHEDULE_NOT_FOUND", $"Schedule {scheduleId} not found"));
        return Ok(ScheduleResponse.From(schedule));
    }

    [HttpPatch("schedules/{scheduleId}")]
    public async Task<ActionResult<ScheduleResponse>> UpdateSchedule(
        Guid scheduleId,
        [FromBody] UpdateScheduleRequest req,
        [FromServices] ScheduleService schedules,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtimeParamsJson = req.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
                ? req.RuntimeParams.Value.GetRawText()
                : null;
            var schedule = await schedules.UpdateAsync(
                scheduleId, req.CronExpression, req.TimeZoneId, req.Enabled, runtimeParamsJson, cancellationToken);
            return Ok(ScheduleResponse.From(schedule));
        }
        catch (CronScheduleException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("SCHEDULE_NOT_FOUND", $"Schedule {scheduleId} not found"));
        }
    }

    [HttpDelete("schedules/{scheduleId}")]
    public async Task<ActionResult> DeleteSchedule(
        Guid scheduleId,
        [FromServices] ScheduleService schedules,
        CancellationToken cancellationToken)
    {
        try
        {
            await schedules.DeleteAsync(scheduleId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException)
        {
            return NotFound(new ApiError("SCHEDULE_NOT_FOUND", $"Schedule {scheduleId} not found"));
        }
    }
}

public record CreateScheduleRequest(
    string CronExpression,
    string? TimeZoneId = null,
    bool? Enabled = null,
    System.Text.Json.JsonElement? RuntimeParams = null);

public record UpdateScheduleRequest(
    string? CronExpression = null,
    string? TimeZoneId = null,
    bool? Enabled = null,
    System.Text.Json.JsonElement? RuntimeParams = null);

public class ScheduleResponse
{
    public Guid Id { get; init; }
    public Guid PipelineId { get; init; }
    public string CronExpression { get; init; } = null!;
    public string TimeZoneId { get; init; } = null!;
    public bool Enabled { get; init; }
    public DateTime? LastFiredAt { get; init; }
    public DateTime? NextFireAt { get; init; }
    public string? RuntimeParamsJson { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static ScheduleResponse From(Schedule s) => new()
    {
        Id = s.Id,
        PipelineId = s.PipelineId,
        CronExpression = s.CronExpression,
        TimeZoneId = s.TimeZoneId,
        Enabled = s.Enabled,
        LastFiredAt = s.LastFiredAt,
        NextFireAt = s.NextFireAt,
        RuntimeParamsJson = s.RuntimeParamsJson,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
    };
}

public record CreatePipelineRequest(
    string Name,
    string? Description,
    List<CreatePipelineStep> Steps);

/// <summary>Replace a pipeline definition as a new version (P1-10). Same shape
/// as create plus an optional optimistic-concurrency guard.</summary>
public record UpdatePipelineRequest(
    string Name,
    string? Description,
    List<CreatePipelineStep> Steps,
    int? ExpectedVersion = null);

public record CreatePipelineStep(
    string Name,
    Guid TaskDefinitionId,
    List<string>? DependsOn = null,
    DispatchStrategy? Strategy = null,
    Guid? TargetNodeId = null,
    Dictionary<string, string>? TargetTags = null,
    StepFailurePolicy FailurePolicy = StepFailurePolicy.FailPipeline,
    int Order = 0,
    List<OutputMapping>? OutputMappings = null,
    System.Text.Json.JsonElement? RuntimeParams = null);

public record StartPipelineRunRequest(
    System.Text.Json.JsonElement? RuntimeParams = null,
    string? TriggerReason = null);

public class PipelineResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int Version { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<PipelineStepResponse> Steps { get; init; } = new();

    public static PipelineResponse From(Pipeline p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Version = p.Version,
        IsDeleted = p.IsDeleted,
        DeletedAt = p.DeletedAt,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Steps = p.Steps.OrderBy(s => s.Order).Select(PipelineStepResponse.From).ToList(),
    };
}

public class PipelineStepResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public Guid TaskDefinitionId { get; init; }
    public List<Guid> DependsOn { get; init; } = new();
    public DispatchStrategy? StrategyOverride { get; init; }
    public Guid? TargetNodeId { get; init; }
    public string? TargetTagsJson { get; init; }
    public StepFailurePolicy FailurePolicy { get; init; }
    public int Order { get; init; }
    public List<OutputMapping>? OutputMappings { get; init; }
    public string? RuntimeParamsJson { get; init; }

    public static PipelineStepResponse From(PipelineStep s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        TaskDefinitionId = s.TaskDefinitionId,
        DependsOn = PipelineGraph.DependencyDecoder.Decode(s.DependsOnJson).ToList(),
        StrategyOverride = s.StrategyOverride,
        TargetNodeId = s.TargetNodeId,
        TargetTagsJson = s.TargetTagsJson,
        FailurePolicy = s.FailurePolicy,
        Order = s.Order,
        OutputMappings = string.IsNullOrEmpty(s.OutputMappingsJson)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<List<OutputMapping>>(s.OutputMappingsJson),
        RuntimeParamsJson = s.RuntimeParamsJson,
    };
}

public class PipelineRunResponse
{
    public Guid Id { get; init; }
    public Guid PipelineId { get; init; }
    public int PipelineVersion { get; init; }
    public string Status { get; init; } = null!;
    public string? TriggerReason { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }

    public static PipelineRunResponse From(PipelineRun r) => new()
    {
        Id = r.Id,
        PipelineId = r.PipelineId,
        PipelineVersion = r.PipelineVersion,
        Status = r.Status.ToString(),
        TriggerReason = r.TriggerReason,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        ErrorMessage = r.ErrorMessage,
    };
}

public class PipelineStepInstanceResponse
{
    public Guid Id { get; init; }
    public Guid PipelineRunId { get; init; }
    public Guid PipelineStepId { get; init; }
    public Guid? TaskInstanceId { get; init; }
    public string Status { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
    public DateTime? DispatchedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ResultJson { get; init; }

    public static PipelineStepInstanceResponse From(PipelineStepInstance s) => new()
    {
        Id = s.Id,
        PipelineRunId = s.PipelineRunId,
        PipelineStepId = s.PipelineStepId,
        TaskInstanceId = s.TaskInstanceId,
        Status = s.Status.ToString(),
        CreatedAt = s.CreatedAt,
        DispatchedAt = s.DispatchedAt,
        CompletedAt = s.CompletedAt,
        ErrorMessage = s.ErrorMessage,
        ResultJson = s.ResultJson,
    };
}

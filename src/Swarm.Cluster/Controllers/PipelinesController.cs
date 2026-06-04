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
    private readonly ILogger<PipelinesController> _logger;

    public PipelinesController(PipelineService service, ILogger<PipelinesController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PipelineResponse>>> List([FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        var (items, total) = await _service.ListAsync(page.Skip, page.NormalizedPageSize, cancellationToken);
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
            var stepDefs = req.Steps.Select(s => new PipelineService.StepDefinition(
                Name: s.Name,
                TaskDefinitionId: s.TaskDefinitionId,
                DependsOnByName: s.DependsOn ?? new List<string>(),
                StrategyOverride: s.Strategy,
                TargetNodeId: s.TargetNodeId,
                TargetTags: s.TargetTags,
                FailurePolicy: s.FailurePolicy,
                Order: s.Order)).ToList();
            var pipeline = await _service.CreateAsync(req.Name, req.Description, stepDefs, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = pipeline.Id }, PipelineResponse.From(pipeline));
        }
        catch (PipelineGraphException ex)
        {
            return BadRequest(new ApiError(ex.Code, ex.Message));
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
}

public record CreatePipelineRequest(
    string Name,
    string? Description,
    List<CreatePipelineStep> Steps);

public record CreatePipelineStep(
    string Name,
    Guid TaskDefinitionId,
    List<string>? DependsOn = null,
    DispatchStrategy? Strategy = null,
    Guid? TargetNodeId = null,
    Dictionary<string, string>? TargetTags = null,
    StepFailurePolicy FailurePolicy = StepFailurePolicy.FailPipeline,
    int Order = 0);

public record StartPipelineRunRequest(
    System.Text.Json.JsonElement? RuntimeParams = null,
    string? TriggerReason = null);

public class PipelineResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<PipelineStepResponse> Steps { get; init; } = new();

    public static PipelineResponse From(Pipeline p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Version = p.Version,
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
    };
}

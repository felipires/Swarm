using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly ClusterDbContext _db;
    private readonly TaskDispatchService _dispatch;
    private readonly ILogger<TasksController> _logger;

    public TasksController(ClusterDbContext db, TaskDispatchService dispatch, ILogger<TasksController> logger)
    {
        _db = db;
        _dispatch = dispatch;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<TaskDefinitionResponse>>> GetAll([FromQuery] PageRequest page)
    {
        var baseQuery = _db.TaskDefinitions.OrderByDescending(t => t.CreatedAt);
        var total = await baseQuery.CountAsync();
        var items = await baseQuery
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .Select(t => ToResponse(t))
            .ToListAsync();

        return Ok(new PagedResult<TaskDefinitionResponse>(items, total, page.NormalizedPage, page.NormalizedPageSize));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskDefinitionResponse>> Get(Guid id)
    {
        var t = await _db.TaskDefinitions.FindAsync(id);
        if (t == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));
        return Ok(ToResponse(t));
    }

    [HttpPost]
    public async Task<ActionResult<TaskDefinitionResponse>> Create([FromBody] CreateTaskRequest req)
    {
        var task = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            TaskType = req.TaskType,
            ConfigJson = req.ConfigJson,
            DefaultStrategy = req.DefaultStrategy,
            DefaultTargetTagsJson = req.DefaultTargetTags is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(req.DefaultTargetTags)
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TaskDefinitions.Add(task);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created task definition {Id} '{Name}'", task.Id, task.Name);

        return CreatedAtAction(nameof(Get), new { id = task.Id }, ToResponse(task));
    }

    private static TaskDefinitionResponse ToResponse(TaskDefinition t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        TaskType = t.TaskType,
        ConfigJson = t.ConfigJson,
        DefaultStrategy = t.DefaultStrategy,
        DefaultTargetTagsJson = t.DefaultTargetTagsJson,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var task = await _db.TaskDefinitions.FindAsync(id);
        if (task == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));

        _db.TaskDefinitions.Remove(task);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Dispatch a task instance. Strategy defaults to the TaskDefinition's
    /// <c>DefaultStrategy</c>; tag selector and target NodeId can override
    /// the definition-level defaults.
    /// </summary>
    [HttpPost("{id}/dispatch")]
    public async Task<ActionResult<TaskInstanceResponse>> Dispatch(Guid id, [FromBody] DispatchTaskRequest req)
    {
        // ErrorHandlingMiddleware (P3-2) translates exceptions thrown below
        // into a structured ApiError response. No per-action try/catch needed.
        var runtimeParamsJson = req.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
            ? req.RuntimeParams.Value.GetRawText()
            : null;
        var instance = await _dispatch.DispatchAsync(id, req.NodeId, req.Strategy, req.TargetTags, runtimeParamsJson);
        return Ok(TaskInstanceResponse.From(instance));
    }

    /// <summary>Dispatch a task to all currently online nodes.</summary>
    [HttpPost("{id}/dispatch-all")]
    public async Task<ActionResult<List<TaskInstanceResponse>>> DispatchAll(Guid id)
    {
        var instances = await _dispatch.DispatchToAllOnlineAsync(id);
        return Ok(instances.Select(TaskInstanceResponse.From));
    }

    /// <summary>Get instances for a task definition (paginated).</summary>
    [HttpGet("{id}/instances")]
    public async Task<ActionResult<PagedResult<TaskInstanceResponse>>> GetInstances(Guid id, [FromQuery] PageRequest page)
    {
        var baseQuery = _db.TaskInstances
            .Where(i => i.TaskDefinitionId == id)
            .OrderByDescending(i => i.CreatedAt);
        var total = await baseQuery.CountAsync();
        var instances = await baseQuery
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync();

        return Ok(new PagedResult<TaskInstanceResponse>(
            instances.Select(TaskInstanceResponse.From).ToList(),
            total, page.NormalizedPage, page.NormalizedPageSize));
    }

    /// <summary>Get a single task instance by ID.</summary>
    [HttpGet("instances/{instanceId}")]
    public async Task<ActionResult<TaskInstanceResponse>> GetInstance(Guid instanceId)
    {
        var instance = await _db.TaskInstances.FindAsync(instanceId);
        if (instance == null) return NotFound(new ApiError("INSTANCE_NOT_FOUND", $"TaskInstance {instanceId} not found"));
        return Ok(TaskInstanceResponse.From(instance));
    }
}

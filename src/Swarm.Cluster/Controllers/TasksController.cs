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
    public async Task<ActionResult<List<TaskDefinitionResponse>>> GetAll()
    {
        var tasks = await _db.TaskDefinitions
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => ToResponse(t))
            .ToListAsync();

        return Ok(tasks);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskDefinitionResponse>> Get(Guid id)
    {
        var t = await _db.TaskDefinitions.FindAsync(id);
        if (t == null) return NotFound();
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
        if (task == null) return NotFound();

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
        try
        {
            var runtimeParamsJson = req.RuntimeParams is { ValueKind: System.Text.Json.JsonValueKind.Object }
                ? req.RuntimeParams.Value.GetRawText()
                : null;
            var instance = await _dispatch.DispatchAsync(id, req.NodeId, req.Strategy, req.TargetTags, runtimeParamsJson);
            return Ok(TaskInstanceResponse.From(instance));
        }
        catch (Swarm.Cluster.Validation.DispatchValidationException ex)
        {
            return BadRequest(new { code = ex.Code, error = ex.Message, details = ex.Details });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Dispatch a task to all currently online nodes.</summary>
    [HttpPost("{id}/dispatch-all")]
    public async Task<ActionResult<List<TaskInstanceResponse>>> DispatchAll(Guid id)
    {
        try
        {
            var instances = await _dispatch.DispatchToAllOnlineAsync(id);
            return Ok(instances.Select(TaskInstanceResponse.From));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Get all instances for a task definition.</summary>
    [HttpGet("{id}/instances")]
    public async Task<ActionResult<List<TaskInstanceResponse>>> GetInstances(Guid id)
    {
        var instances = await _db.TaskInstances
            .Where(i => i.TaskDefinitionId == id)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(instances.Select(TaskInstanceResponse.From));
    }

    /// <summary>Get a single task instance by ID.</summary>
    [HttpGet("instances/{instanceId}")]
    public async Task<ActionResult<TaskInstanceResponse>> GetInstance(Guid instanceId)
    {
        var instance = await _db.TaskInstances.FindAsync(instanceId);
        if (instance == null) return NotFound();
        return Ok(TaskInstanceResponse.From(instance));
    }
}

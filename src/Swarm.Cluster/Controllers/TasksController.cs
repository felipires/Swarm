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
    private readonly EntityVersionService _versions;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        ClusterDbContext db,
        TaskDispatchService dispatch,
        EntityVersionService versions,
        ILogger<TasksController> logger)
    {
        _db = db;
        _dispatch = dispatch;
        _versions = versions;
        _logger = logger;
    }

    /// <summary>The create-request-shaped snapshot persisted per task version.</summary>
    private static CreateTaskRequest SnapshotOf(TaskDefinition t) => new(
        t.Name, t.Description, t.TaskType, t.ConfigJson, t.DefaultStrategy,
        string.IsNullOrEmpty(t.DefaultTargetTagsJson)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(t.DefaultTargetTagsJson));

    [HttpGet]
    public async Task<ActionResult<PagedResult<TaskDefinitionResponse>>> GetAll(
        [FromQuery] PageRequest page, [FromQuery] bool includeDeleted)
    {
        // includeDeleted bypasses the soft-delete filter so operators can see + restore.
        var set = includeDeleted ? _db.TaskDefinitions.IgnoreQueryFilters() : _db.TaskDefinitions;
        var baseQuery = set.OrderByDescending(t => t.CreatedAt);
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
        // FirstOrDefault (not Find) so the soft-delete query filter applies.
        var t = await _db.TaskDefinitions.FirstOrDefaultAsync(t => t.Id == id);
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
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TaskDefinitions.Add(task);
        _versions.Record(VersionedEntityType.Task, task.Id, task.Version, SnapshotOf(task));
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created task definition {Id} '{Name}' v{Version}", task.Id, task.Name, task.Version);

        return CreatedAtAction(nameof(Get), new { id = task.Id }, ToResponse(task));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TaskDefinitionResponse>> Update(Guid id, [FromBody] UpdateTaskRequest req)
    {
        var task = await _db.TaskDefinitions.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));

        if (req.ExpectedVersion is { } expected && expected != task.Version)
            return Conflict(new ApiError("VERSION_CONFLICT",
                $"Task is at version {task.Version}, not {expected}; reload before editing"));

        task.Name = req.Name;
        task.Description = req.Description;
        task.TaskType = req.TaskType;
        task.ConfigJson = req.ConfigJson;
        task.DefaultStrategy = req.DefaultStrategy;
        task.DefaultTargetTagsJson = req.DefaultTargetTags is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(req.DefaultTargetTags)
            : null;
        task.Version += 1;
        task.UpdatedAt = DateTime.UtcNow;

        _versions.Record(VersionedEntityType.Task, task.Id, task.Version, SnapshotOf(task));
        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated task definition {Id} → v{Version}", task.Id, task.Version);
        return Ok(ToResponse(task));
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
        Version = t.Version,
        IsDeleted = t.IsDeleted,
        DeletedAt = t.DeletedAt,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var task = await _db.TaskDefinitions.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));

        // Soft delete (P1-10): hidden by the global query filter; instances + history survive.
        task.IsDeleted = true;
        task.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/undelete")]
    public async Task<ActionResult> Undelete(Guid id)
    {
        var task = await _db.TaskDefinitions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));

        task.IsDeleted = false;
        task.DeletedAt = null;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Version history (P1-10) ---

    [HttpGet("{id}/versions")]
    public async Task<ActionResult<List<EntityVersionResponse>>> ListVersions(Guid id, CancellationToken ct)
    {
        if (!await _db.TaskDefinitions.AnyAsync(t => t.Id == id, ct))
            return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));
        var rows = await _versions.ListAsync(VersionedEntityType.Task, id, ct);
        return Ok(rows.Select(EntityVersionResponse.Meta).ToList());
    }

    [HttpGet("{id}/versions/{version:int}")]
    public async Task<ActionResult<EntityVersionResponse>> GetVersion(Guid id, int version, CancellationToken ct)
    {
        var row = await _versions.GetAsync(VersionedEntityType.Task, id, version, ct);
        if (row == null) return NotFound(new ApiError("VERSION_NOT_FOUND", $"Task {id} has no version {version}"));
        return Ok(EntityVersionResponse.Full(row));
    }

    [HttpPost("{id}/versions/{version:int}/restore")]
    public async Task<ActionResult<TaskDefinitionResponse>> RestoreVersion(Guid id, int version, CancellationToken ct)
    {
        var task = await _db.TaskDefinitions.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task == null) return NotFound(new ApiError("TASK_NOT_FOUND", $"TaskDefinition {id} not found"));

        var row = await _versions.GetAsync(VersionedEntityType.Task, id, version, ct);
        if (row == null) return NotFound(new ApiError("VERSION_NOT_FOUND", $"Task {id} has no version {version}"));

        var snap = EntityVersionService.Deserialize<CreateTaskRequest>(row.SnapshotJson);
        task.Name = snap.Name;
        task.Description = snap.Description;
        task.TaskType = snap.TaskType;
        task.ConfigJson = snap.ConfigJson;
        task.DefaultStrategy = snap.DefaultStrategy;
        task.DefaultTargetTagsJson = snap.DefaultTargetTags is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(snap.DefaultTargetTags)
            : null;
        task.Version += 1;
        task.UpdatedAt = DateTime.UtcNow;

        _versions.Record(VersionedEntityType.Task, task.Id, task.Version, SnapshotOf(task));
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Restored task {Id} version {From} as v{To}", task.Id, version, task.Version);
        return Ok(ToResponse(task));
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

    /// <summary>
    /// Get instances for a task definition. Two pagination modes share this
    /// route (P3-1): the default offset mode (<c>?page=&amp;pageSize=</c>,
    /// returns a total count) and an opt-in keyset/cursor mode
    /// (<c>?cursor=true&amp;after=&amp;limit=</c>) for stable deep paging on this
    /// high-frequency endpoint. Cursor mode engages when <c>cursor=true</c> or an
    /// <c>after</c> token is supplied; otherwise the offset path is unchanged.
    /// </summary>
    [HttpGet("{id}/instances")]
    public async Task<ActionResult> GetInstances(
        Guid id,
        [FromQuery] PageRequest page,
        [FromQuery] CursorRequest cursor,
        [FromQuery] bool useCursor = false)
    {
        if (useCursor || !string.IsNullOrEmpty(cursor.After))
            return await GetInstancesByCursorAsync(id, cursor);

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

    private async Task<ActionResult> GetInstancesByCursorAsync(Guid id, CursorRequest cursor)
    {
        var query = _db.TaskInstances.Where(i => i.TaskDefinitionId == id);

        if (cursor.After is not null)
        {
            if (!Cursor.TryDecode(cursor.After, out var key))
                return BadRequest(new ApiError("INVALID_CURSOR", "The 'after' cursor is malformed"));
            // Keyset boundary: strictly older than the cursor under the
            // (CreatedAt DESC, Id DESC) ordering. Expanded form (rather than a
            // tuple comparison) for reliable provider translation.
            query = query.Where(i =>
                i.CreatedAt < key.CreatedAt
                || (i.CreatedAt == key.CreatedAt && i.Id.CompareTo(key.Id) < 0));
        }

        var limit = cursor.NormalizedLimit;
        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Take(limit + 1)   // one extra to detect HasMore
            .ToListAsync();

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows;
        var nextCursor = hasMore && page.Count > 0
            ? Cursor.Encode(new Cursor.Key(page[^1].CreatedAt, page[^1].Id))
            : null;

        return Ok(new CursorPagedResult<TaskInstanceResponse>(
            page.Select(TaskInstanceResponse.From).ToList(), nextCursor, hasMore));
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

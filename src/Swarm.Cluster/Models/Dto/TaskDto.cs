namespace Swarm.Cluster.Models.Dto;

public record CreateTaskRequest(
    string Name,
    string? Description,
    string TaskType = "default@1",
    string ConfigJson = "{}",
    DispatchStrategy DefaultStrategy = DispatchStrategy.SpecificNode,
    Dictionary<string, string>? DefaultTargetTags = null);

/// <summary>
/// Dispatch a task instance. All fields are optional — when omitted the
/// TaskDefinition's <c>DefaultStrategy</c> and <c>DefaultTargetTags</c> apply.
/// <c>RuntimeParams</c> is a per-run JSON object resolved by handlers via
/// <c>{param:key}</c> placeholders (P1-6).
/// </summary>
public record DispatchTaskRequest(
    Guid? NodeId = null,
    DispatchStrategy? Strategy = null,
    Dictionary<string, string>? TargetTags = null,
    System.Text.Json.JsonElement? RuntimeParams = null);

public class TaskDefinitionResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public string TaskType { get; init; } = "default@1";
    public string ConfigJson { get; init; } = "{}";
    public DispatchStrategy DefaultStrategy { get; init; }
    public string? DefaultTargetTagsJson { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public class TaskInstanceResponse
{
    public Guid Id { get; init; }
    public Guid TaskDefinitionId { get; init; }

    /// <summary>NULL until a Node claims a shared-queue dispatch (D1).</summary>
    public Guid? NodeId { get; init; }

    public string Status { get; init; } = null!;
    public string? ResultJson { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DispatchedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    public static TaskInstanceResponse From(TaskInstance i) => new()
    {
        Id = i.Id,
        TaskDefinitionId = i.TaskDefinitionId,
        NodeId = i.NodeId,
        Status = i.Status.ToString(),
        ResultJson = i.ResultJson,
        ErrorMessage = i.ErrorMessage,
        CreatedAt = i.CreatedAt,
        DispatchedAt = i.DispatchedAt,
        CompletedAt = i.CompletedAt
    };
}

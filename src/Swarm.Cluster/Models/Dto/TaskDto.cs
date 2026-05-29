using Swarm.Cluster.Models;

namespace Swarm.Cluster.Models.Dto;

public record CreateTaskRequest(string Name, string? Description, string ConfigJson = "{}");

public record DispatchTaskRequest(Guid NodeId);

public class TaskDefinitionResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public string ConfigJson { get; init; } = "{}";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public class TaskInstanceResponse
{
    public Guid Id { get; init; }
    public Guid TaskDefinitionId { get; init; }
    public Guid NodeId { get; init; }
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

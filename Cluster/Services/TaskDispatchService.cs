using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Records a dispatch decision atomically. Inserts the <see cref="TaskInstance"/>
/// and a matching <see cref="PendingDispatch"/> row inside one DB transaction
/// (roadmap P0-4 outbox pattern). The actual RabbitMQ publish runs out-of-band
/// in <see cref="OutboxPublisherService"/>. There is no synchronous publish
/// path here — if there were, a publish failure after the instance was
/// persisted would orphan the row.
/// </summary>
public class TaskDispatchService
{
    private readonly ClusterDbContext _dbContext;
    private readonly ILogger<TaskDispatchService> _logger;

    public TaskDispatchService(ClusterDbContext dbContext, ILogger<TaskDispatchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<TaskInstance> DispatchAsync(Guid taskDefinitionId, Guid nodeId)
    {
        var definition = await _dbContext.TaskDefinitions.FindAsync(taskDefinitionId)
            ?? throw new InvalidOperationException($"TaskDefinition {taskDefinitionId} not found");

        var node = await _dbContext.Nodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Node {nodeId} not found");

        if (node.Status != Node.NodeStatus.Online)
            throw new InvalidOperationException($"Node {nodeId} is not online");

        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = taskDefinitionId,
            NodeId = nodeId,
            CreatedAt = DateTime.UtcNow,
        };

        var message = BuildMessage(instance, definition);
        var pending = new PendingDispatch
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            QueueName = TaskQueueName(nodeId),
            Payload = JsonSerializer.Serialize(message),
            CreatedAt = DateTime.UtcNow,
        };

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        _dbContext.TaskInstances.Add(instance);
        _dbContext.PendingDispatches.Add(pending);
        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "Enqueued task instance {InstanceId} for dispatch to node {NodeId}", instance.Id, nodeId);
        return instance;
    }

    public async Task<List<TaskInstance>> DispatchToAllOnlineAsync(Guid taskDefinitionId)
    {
        var onlineNodes = await _dbContext.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online)
            .Select(n => n.Id)
            .ToListAsync();

        if (onlineNodes.Count == 0)
            throw new InvalidOperationException("No online nodes available");

        var results = new List<TaskInstance>();
        foreach (var nodeId in onlineNodes)
            results.Add(await DispatchAsync(taskDefinitionId, nodeId));

        return results;
    }

    internal static TaskMessage BuildMessage(TaskInstance instance, TaskDefinition definition) => new()
    {
        InstanceId = instance.Id,
        TaskDefinitionId = definition.Id,
        NodeId = instance.NodeId,
        TaskType = definition.TaskType,
        ConfigJson = definition.ConfigJson,
    };

    public static string TaskQueueName(Guid nodeId) => $"tasks.{nodeId}";
}

public record TaskMessage
{
    public Guid InstanceId { get; init; }
    public Guid TaskDefinitionId { get; init; }
    public Guid NodeId { get; init; }

    /// <summary>
    /// Task type identifier including version (roadmap D3). Defaults to
    /// <c>"default@1"</c> so messages from a pre-versioning Cluster build
    /// remain wire-compatible on the Node side.
    /// </summary>
    public string TaskType { get; init; } = "default@1";

    public string ConfigJson { get; init; } = "{}";
}

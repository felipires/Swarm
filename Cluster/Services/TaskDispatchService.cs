using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

public class TaskDispatchService
{
    private readonly ClusterDbContext _dbContext;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<TaskDispatchService> _logger;

    public TaskDispatchService(ClusterDbContext dbContext, IConnection rabbitConnection, ILogger<TaskDispatchService> logger)
    {
        _dbContext = dbContext;
        _rabbitConnection = rabbitConnection;
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
            Status = TaskInstance.TaskInstanceStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.TaskInstances.Add(instance);
        await _dbContext.SaveChangesAsync();

        var message = new TaskMessage
        {
            InstanceId = instance.Id,
            TaskDefinitionId = taskDefinitionId,
            NodeId = nodeId,
            ConfigJson = definition.ConfigJson
        };

        PublishToNode(nodeId, message);

        instance.Status = TaskInstance.TaskInstanceStatus.Dispatched;
        instance.DispatchedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dispatched task instance {InstanceId} to node {NodeId}", instance.Id, nodeId);
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

    private void PublishToNode(Guid nodeId, TaskMessage message)
    {
        using var channel = _rabbitConnection.CreateModel();
        var queueName = TaskQueueName(nodeId);

        channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        channel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: props, body: body);
    }

    public static string TaskQueueName(Guid nodeId) => $"tasks.{nodeId}";
}

public record TaskMessage
{
    public Guid InstanceId { get; init; }
    public Guid TaskDefinitionId { get; init; }
    public Guid NodeId { get; init; }
    public string ConfigJson { get; init; } = "{}";
}

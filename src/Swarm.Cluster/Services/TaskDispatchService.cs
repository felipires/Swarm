using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Validation;
using Swarm.Sdk.Wire;

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
    private readonly DispatchValidator? _validator;
    private readonly Random _picker = new();

    /// <summary>
    /// Two-arg constructor for tests that want to bypass the dispatch validator
    /// (those tests assert routing/persistence behavior independently).
    /// </summary>
    public TaskDispatchService(ClusterDbContext dbContext, ILogger<TaskDispatchService> logger)
        : this(dbContext, logger, validator: null) { }

    public TaskDispatchService(ClusterDbContext dbContext, ILogger<TaskDispatchService> logger, DispatchValidator? validator)
    {
        _dbContext = dbContext;
        _logger = logger;
        _validator = validator;
    }

    /// <summary>
    /// Dispatch by strategy (D1). For per-node strategies the NodeId is set
    /// immediately and the message goes to <c>tasks.&lt;nodeId&gt;</c>. For
    /// <see cref="DispatchStrategy.AnyOnlineNode"/> the message goes to a
    /// shared queue with NodeId NULL; the picking Node sends a claim that
    /// <c>TaskClaimsConsumerService</c> turns into the bound NodeId.
    /// </summary>
    public async Task<TaskInstance> DispatchAsync(
        Guid taskDefinitionId,
        Guid? nodeId = null,
        DispatchStrategy? strategy = null,
        IReadOnlyDictionary<string, string>? targetTags = null,
        string? runtimeParamsJson = null)
    {
        var definition = await _dbContext.TaskDefinitions.FindAsync(taskDefinitionId)
            ?? throw new InvalidOperationException($"TaskDefinition {taskDefinitionId} not found");

        var effectiveStrategy = strategy ?? definition.DefaultStrategy;
        var effectiveTags = targetTags ?? ParseTags(definition.DefaultTargetTagsJson);

        // P1-7: fail-fast validation before persisting/publishing.
        if (_validator is not null)
            await _validator.ValidateAsync(definition, runtimeParamsJson, effectiveStrategy, nodeId, CancellationToken.None);

        return effectiveStrategy switch
        {
            DispatchStrategy.SpecificNode  => await DispatchToSpecificAsync(definition, nodeId
                ?? throw new InvalidOperationException("SpecificNode strategy requires a NodeId"), runtimeParamsJson),
            DispatchStrategy.AnyOnlineNode => await DispatchToSharedAsync(definition, runtimeParamsJson),
            DispatchStrategy.TaggedNodes   => await DispatchTaggedAsync(definition, effectiveTags, runtimeParamsJson),
            DispatchStrategy.AllOnlineNodes => throw new InvalidOperationException(
                "AllOnlineNodes is a broadcast strategy — use DispatchToAllOnlineAsync instead"),
            _ => throw new InvalidOperationException($"Unknown dispatch strategy {effectiveStrategy}"),
        };
    }

    private async Task<TaskInstance> DispatchToSpecificAsync(TaskDefinition definition, Guid nodeId, string? runtimeParamsJson = null)
    {
        var node = await _dbContext.Nodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Node {nodeId} not found");
        if (node.Status != Node.NodeStatus.Online)
            throw new InvalidOperationException($"Node {nodeId} is not online");

        var instance = NewInstance(definition.Id, nodeId, runtimeParamsJson);
        await PersistDispatchAsync(instance, definition, TaskQueueName(nodeId));
        _logger.LogInformation(
            "Enqueued task instance {InstanceId} for dispatch to node {NodeId}", instance.Id, nodeId);
        return instance;
    }

    private async Task<TaskInstance> DispatchToSharedAsync(TaskDefinition definition, string? runtimeParamsJson)
    {
        // Sanity: at least one Online Node must advertise the TaskType.
        // Without this check the message would sit in the shared queue forever.
        var hasConsumer = await _dbContext.NodeCapabilities
            .Join(_dbContext.Nodes, c => c.NodeId, n => n.Id, (c, n) => new { c, n })
            .AnyAsync(x => x.c.TaskType == definition.TaskType && x.n.Status == Node.NodeStatus.Online);
        if (!hasConsumer)
            throw new InvalidOperationException(
                $"No online Node currently advertises TaskType '{definition.TaskType}'");

        var instance = NewInstance(definition.Id, nodeId: null, runtimeParamsJson);
        await PersistDispatchAsync(instance, definition, SharedQueueName(definition.TaskType));
        _logger.LogInformation(
            "Enqueued task instance {InstanceId} on shared queue for TaskType {TaskType}",
            instance.Id, definition.TaskType);
        return instance;
    }

    private async Task<TaskInstance> DispatchTaggedAsync(TaskDefinition definition, IReadOnlyDictionary<string, string>? tags, string? runtimeParamsJson)
    {
        if (tags is null || tags.Count == 0)
            throw new InvalidOperationException("TaggedNodes strategy requires a non-empty tag selector");

        // P0-3a Phase-1 simplification: resolve eligible Nodes server-side and
        // pick one at random. Full shared queue (tasks.tagged.<hash>) with
        // dynamic Node subscription via heartbeat push is a follow-up.
        var candidates = await _dbContext.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online && n.StaticTagsJson != null)
            .ToListAsync();

        var eligible = new List<Node>();
        foreach (var node in candidates)
        {
            var nodeTags = ParseTags(node.StaticTagsJson);
            if (nodeTags is null) continue;
            if (tags.All(req => nodeTags.TryGetValue(req.Key, out var v) && v == req.Value))
                eligible.Add(node);
        }

        if (eligible.Count == 0)
            throw new InvalidOperationException(
                $"No online Node satisfies tag selector {JsonSerializer.Serialize(tags)}");

        var picked = eligible[_picker.Next(eligible.Count)];
        _logger.LogInformation(
            "TaggedNodes resolved to {Eligible} eligible Nodes; picking {NodeId}",
            eligible.Count, picked.Id);
        return await DispatchToSpecificAsync(definition, picked.Id, runtimeParamsJson);
    }

    public async Task<List<TaskInstance>> DispatchToAllOnlineAsync(Guid taskDefinitionId)
    {
        var definition = await _dbContext.TaskDefinitions.FindAsync(taskDefinitionId)
            ?? throw new InvalidOperationException($"TaskDefinition {taskDefinitionId} not found");

        var onlineNodes = await _dbContext.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online)
            .Select(n => n.Id)
            .ToListAsync();

        if (onlineNodes.Count == 0)
            throw new InvalidOperationException("No online nodes available");

        var results = new List<TaskInstance>();
        foreach (var nodeId in onlineNodes)
            results.Add(await DispatchToSpecificAsync(definition, nodeId));

        return results;
    }

    private TaskInstance NewInstance(Guid taskDefinitionId, Guid? nodeId, string? runtimeParamsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        TaskDefinitionId = taskDefinitionId,
        NodeId = nodeId,
        RuntimeParamsJson = runtimeParamsJson,
        CreatedAt = DateTime.UtcNow,
    };

    private async Task PersistDispatchAsync(TaskInstance instance, TaskDefinition definition, string queueName)
    {
        var message = BuildMessage(instance, definition);
        var pending = new PendingDispatch
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            QueueName = queueName,
            Payload = JsonSerializer.Serialize(message),
            CreatedAt = DateTime.UtcNow,
        };

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        _dbContext.TaskInstances.Add(instance);
        _dbContext.PendingDispatches.Add(pending);
        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();
    }

    internal static TaskMessage BuildMessage(TaskInstance instance, TaskDefinition definition) => new()
    {
        InstanceId = instance.Id,
        TaskDefinitionId = definition.Id,
        NodeId = instance.NodeId,
        TaskType = definition.TaskType,
        ConfigJson = definition.ConfigJson,
        RuntimeParamsJson = instance.RuntimeParamsJson,
    };

    private static Dictionary<string, string>? ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException) { return null; }
    }

    public static string TaskQueueName(Guid nodeId) => $"tasks.{nodeId}";
    public static string SharedQueueName(string taskType) => $"tasks.shared.{taskType}";
}

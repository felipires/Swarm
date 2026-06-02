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

        // Determinism: same tag selector → same queue. The Cluster also
        // remembers the route in TaggedRoute so it can tell Nodes which
        // tagged queues to subscribe to (via heartbeat response).
        var (hash, canonical) = TaggedRouteHash.Compute(tags);
        var queueName = TaggedRouteHash.QueueNameForHash(hash);

        // Eligibility precheck: at least one online Node must have a
        // superset of the selector tags AND advertise the TaskType.
        var eligible = await ResolveEligibleNodesAsync(tags, definition.TaskType);
        if (eligible.Count == 0)
            throw new InvalidOperationException(
                $"No online Node satisfies tag selector {canonical} for TaskType '{definition.TaskType}'");

        var existing = await _dbContext.TaggedRoutes.FindAsync(hash);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            _dbContext.TaggedRoutes.Add(new TaggedRoute
            {
                Hash = hash,
                SelectorJson = canonical,
                FirstSeenAt = now,
                LastUsedAt = now,
            });
        }
        else
        {
            existing.LastUsedAt = now;
        }

        var instance = NewInstance(definition.Id, nodeId: null, runtimeParamsJson);
        await PersistDispatchAsync(instance, definition, queueName);
        _logger.LogInformation(
            "Enqueued instance {InstanceId} on tagged shared queue {Queue} (selector {Selector}, {Count} eligible Node(s))",
            instance.Id, queueName, canonical, eligible.Count);
        return instance;
    }

    private async Task<List<Node>> ResolveEligibleNodesAsync(IReadOnlyDictionary<string, string> selector, string taskType)
    {
        // Pull online Nodes that advertise the TaskType, then filter by tag
        // superset in-memory (StaticTagsJson is text/jsonb; doing the JSON
        // containment in SQL would require provider-specific syntax — Phase-1
        // simplification, see P3-3 follow-up).
        var candidates = await _dbContext.Nodes
            .Join(_dbContext.NodeCapabilities, n => n.Id, c => c.NodeId, (n, c) => new { Node = n, Capability = c })
            .Where(x => x.Node.Status == Node.NodeStatus.Online
                        && x.Capability.TaskType == taskType
                        && x.Node.StaticTagsJson != null)
            .Select(x => x.Node)
            .Distinct()
            .ToListAsync();

        var eligible = new List<Node>();
        foreach (var node in candidates)
        {
            var nodeTags = ParseTags(node.StaticTagsJson);
            if (nodeTags is null) continue;
            if (selector.All(req => nodeTags.TryGetValue(req.Key, out var v) && v == req.Value))
                eligible.Add(node);
        }
        return eligible;
    }

    /// <summary>
    /// Broadcast: one <see cref="TaskInstance"/> per online Node, each on its
    /// own per-node queue. P2-4 — all rows are batched into a single
    /// <c>AddRange</c> + transaction so a 100-node fleet incurs one round
    /// trip instead of one per Node.
    /// </summary>
    public async Task<List<TaskInstance>> DispatchToAllOnlineAsync(Guid taskDefinitionId, string? runtimeParamsJson = null)
    {
        var definition = await _dbContext.TaskDefinitions.FindAsync(taskDefinitionId)
            ?? throw new InvalidOperationException($"TaskDefinition {taskDefinitionId} not found");

        var onlineNodeIds = await _dbContext.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online)
            .Select(n => n.Id)
            .ToListAsync();

        if (onlineNodeIds.Count == 0)
            throw new InvalidOperationException("No online nodes available");

        // P1-7 once for the broadcast — eligibility check still applies
        // (must have ≥1 online Node advertising the TaskType somewhere).
        if (_validator is not null)
            await _validator.ValidateAsync(definition, runtimeParamsJson,
                DispatchStrategy.AllOnlineNodes, targetNodeId: null, CancellationToken.None);

        var instances = new List<TaskInstance>(onlineNodeIds.Count);
        var pendings = new List<PendingDispatch>(onlineNodeIds.Count);
        foreach (var nodeId in onlineNodeIds)
        {
            var instance = NewInstance(definition.Id, nodeId, runtimeParamsJson);
            instances.Add(instance);
            pendings.Add(new PendingDispatch
            {
                Id = Guid.NewGuid(),
                InstanceId = instance.Id,
                QueueName = TaskQueueName(nodeId),
                Payload = JsonSerializer.Serialize(BuildMessage(instance, definition)),
                CreatedAt = DateTime.UtcNow,
            });
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        _dbContext.TaskInstances.AddRange(instances);
        _dbContext.PendingDispatches.AddRange(pendings);
        await _dbContext.SaveChangesAsync();
        await tx.CommitAsync();

        _logger.LogInformation(
            "Broadcast task {DefinitionId} to {Count} online node(s)", definition.Id, onlineNodeIds.Count);
        return instances;
    }

    private static TaskInstance NewInstance(Guid taskDefinitionId, Guid? nodeId, string? runtimeParamsJson = null) => new()
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

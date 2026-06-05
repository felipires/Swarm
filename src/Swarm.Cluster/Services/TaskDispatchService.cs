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
    private readonly Tags.ITagMatchStrategy _tagMatcher;

    /// <summary>
    /// Two-arg constructor for tests that want to bypass the dispatch validator
    /// (those tests assert routing/persistence behavior independently). The tag
    /// matcher defaults to the in-memory implementation over the same context.
    /// </summary>
    public TaskDispatchService(ClusterDbContext dbContext, ILogger<TaskDispatchService> logger)
        : this(dbContext, logger, validator: null, tagMatcher: null) { }

    public TaskDispatchService(
        ClusterDbContext dbContext,
        ILogger<TaskDispatchService> logger,
        DispatchValidator? validator,
        Tags.ITagMatchStrategy? tagMatcher = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _validator = validator;
        // P3-3: when not injected (unit tests on EF InMemory), fall back to the
        // in-memory matcher — it shares the semantic contract with the Postgres
        // one and reads the same effective-tag state.
        _tagMatcher = tagMatcher ?? new Tags.InMemoryTagMatcher(dbContext);
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

        var instance = NewInstance(definition, nodeId, runtimeParamsJson);
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

        var instance = NewInstance(definition, nodeId: null, runtimeParamsJson);
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
        // superset of the selector tags AND advertise the TaskType. P3-3 runs
        // this through ITagMatchStrategy — GIN-indexed `EffectiveTags @>
        // selector` in Postgres, in-memory LINQ in tests.
        var eligible = await _tagMatcher.MatchEligibleNodesAsync(tags, definition.TaskType, CancellationToken.None);
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

        var instance = NewInstance(definition, nodeId: null, runtimeParamsJson);
        await PersistDispatchAsync(instance, definition, queueName);
        _logger.LogInformation(
            "Enqueued instance {InstanceId} on tagged shared queue {Queue} (selector {Selector}, {Count} eligible Node(s))",
            instance.Id, queueName, canonical, eligible.Count);
        return instance;
    }

    /// <summary>
    /// Broadcast: one <see cref="TaskInstance"/> per online Node, each on its
    /// own per-node queue. P2-4 — all rows are batched into a single
    /// <c>AddRange</c> + transaction so a 100-node fleet incurs one round
    /// trip instead of one per Node.
    /// </summary>
    /// <summary>
    /// Re-emit an existing <see cref="TaskInstance"/> to the broker (P1-2).
    /// Called by <c>RetrySchedulerService</c> when a Pending instance's
    /// <c>RetryAfter</c> is due. Reuses the instance's snapshotted payload —
    /// retries run with the same config as the original attempt.
    /// </summary>
    public async Task RedispatchAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await _dbContext.TaskInstances.FindAsync([instanceId], cancellationToken)
            ?? throw new InvalidOperationException($"TaskInstance {instanceId} not found");
        if (instance.Status != TaskInstance.TaskInstanceStatus.Pending)
            throw new InvalidOperationException(
                $"TaskInstance {instanceId} is not Pending (Status={instance.Status})");

        var queueName = instance.NodeId is { } nodeId
            ? TaskQueueName(nodeId)
            : SharedQueueName(instance.TaskType);

        var pending = new PendingDispatch
        {
            Id = Guid.NewGuid(),
            InstanceId = instance.Id,
            QueueName = queueName,
            Payload = JsonSerializer.Serialize(BuildMessage(instance)),
            CreatedAt = DateTime.UtcNow,
        };

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.PendingDispatches.Add(pending);
        instance.Transition(TaskInstance.TaskInstanceStatus.Dispatched);
        instance.DispatchedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Redispatched task instance {InstanceId} (retry #{RetryCount}) to {Queue}",
            instance.Id, instance.RetryCount, queueName);
    }

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
            var instance = NewInstance(definition, nodeId, runtimeParamsJson);
            instances.Add(instance);
            pendings.Add(new PendingDispatch
            {
                Id = Guid.NewGuid(),
                InstanceId = instance.Id,
                QueueName = TaskQueueName(nodeId),
                Payload = JsonSerializer.Serialize(BuildMessage(instance)),
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

    /// <summary>
    /// Construct a new <see cref="TaskInstance"/> with the TaskDefinition's
    /// TaskType / ConfigJson / Version snapshotted (P1-4). The snapshot is
    /// the authoritative payload for everything downstream — subsequent
    /// edits to the definition don't affect in-flight instances.
    /// </summary>
    private static TaskInstance NewInstance(TaskDefinition definition, Guid? nodeId, string? runtimeParamsJson = null) => new()
    {
        Id = Guid.NewGuid(),
        TaskDefinitionId = definition.Id,
        NodeId = nodeId,
        TaskType = definition.TaskType,
        ConfigJsonSnapshot = definition.ConfigJson,
        TaskDefinitionVersion = definition.Version,
        RuntimeParamsJson = runtimeParamsJson,
        CreatedAt = DateTime.UtcNow,
    };

    private async Task PersistDispatchAsync(TaskInstance instance, TaskDefinition definition, string queueName)
    {
        var message = BuildMessage(instance);
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

    /// <summary>
    /// Build the wire payload from the instance's snapshot (P1-4). The
    /// snapshot is captured at dispatch time so what the Node sees is
    /// immune to subsequent definition edits.
    /// </summary>
    internal static TaskMessage BuildMessage(TaskInstance instance) => new()
    {
        InstanceId = instance.Id,
        TaskDefinitionId = instance.TaskDefinitionId,
        NodeId = instance.NodeId,
        TaskType = instance.TaskType,
        ConfigJson = instance.ConfigJsonSnapshot,
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

using Microsoft.EntityFrameworkCore;
using Serilog;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using System.Text.Json;

namespace Swarm.Cluster.Services;

/// <summary>
/// Manages node registration, heartbeat, and capability discovery
/// </summary>
public class NodeService
{
    private readonly ClusterDbContext _dbContext;
    private readonly ILogger<NodeService> _logger;
    private readonly IConfiguration _config;
    private readonly int _heartbeatTimeoutSeconds;

    public NodeService(ClusterDbContext dbContext, ILogger<NodeService> logger, IConfiguration config)
    {
        _dbContext = dbContext;
        _logger = logger;
        _config = config;
        _heartbeatTimeoutSeconds = config.GetValue<int>("Heartbeat:TimeoutSeconds", 300);
    }

    /// <summary>
    /// Register a new node with capabilities
    /// </summary>
    public async Task<RequestNodeRegistrationResponse> RegisterNodeAsync(
        string apiKey,
        Guid? nodeId,
        Dictionary<string, string>? staticTags = null,
        IReadOnlyList<NodeCapability>? capabilities = null)
    {
        _logger.LogInformation("Registering node: {NodeName}", nodeId);

        var resolvedId = nodeId ?? Guid.NewGuid();
        var staticTagsJson = staticTags is not null ? JsonSerializer.Serialize(staticTags) : null;

        // Update-in-place when the row exists. Building a fresh Node and
        // calling Update would (a) lose CreatedAt and (b) trip the EF change
        // tracker if the entity was already materialized in this scope.
        var node = nodeId is not null
            ? await _dbContext.Nodes.FirstOrDefaultAsync(n => n.Id == resolvedId)
            : null;

        if (node is not null)
        {
            node.Status = Node.NodeStatus.Online;
            node.LastHeartbeatAt = DateTime.UtcNow;
            node.StaticTagsJson = staticTagsJson;
        }
        else
        {
            node = new Node
            {
                Id = resolvedId,
                Name = GenerateNodeName(),
                Status = Node.NodeStatus.Online,
                CreatedAt = DateTime.UtcNow,
                LastHeartbeatAt = DateTime.UtcNow,
                StaticTagsJson = staticTagsJson,
            };
            _dbContext.Nodes.Add(node);
        }

        // P0-3b: capabilities are replaced wholesale on each registration.
        // Wipe the old set, then add the new declarations.
        var stale = await _dbContext.NodeCapabilities
            .Where(c => c.NodeId == node.Id)
            .ToListAsync();
        _dbContext.NodeCapabilities.RemoveRange(stale);

        if (capabilities is { Count: > 0 })
        {
            foreach (var cap in capabilities)
            {
                cap.Id = Guid.NewGuid();
                cap.NodeId = node.Id;
                cap.ReportedAt = DateTime.UtcNow;
                _dbContext.NodeCapabilities.Add(cap);
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Node registered successfully: {NodeId} ({NodeName}) — {CapabilityCount} capabilities reported",
            node.Id, node.Name, capabilities?.Count ?? 0);

        return new RequestNodeRegistrationResponse()
        {
            NodeId = node.Id.ToString(),
            NodeName = node.Name,
            QueueParameters = new()
            {
                QueuePort = _config["RabbitMQ:Port"]!,
                QueueHost = _config["RabbitMQ:Hostname"]!,
                QueuePassword = _config["RabbitMQ:Password"]!,
                QueueUserName = _config["RabbitMQ:UserName"]!,
            }
        };
    }

    /// <summary>
    /// Record heartbeat from a node
    /// </summary>
    public async Task<bool> UpdateHeartbeatAsync(Guid nodeId, bool isOnline = true)
    {
        var node = await _dbContext.Nodes.FindAsync(nodeId);
        if (node == null)
        {
            _logger.LogWarning("Heartbeat received from unknown node: {NodeId}", nodeId);
            return false;
        }

        var wasOffline = node.Status == Node.NodeStatus.Offline;

        node.LastHeartbeatAt = DateTime.UtcNow;
        node.Status = isOnline ? Node.NodeStatus.Online : Node.NodeStatus.Offline;

        if (isOnline && wasOffline)
        {
            _logger.LogInformation("Node came back online: {NodeId}", nodeId);
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Get nodes with optional status filter (paginated, P3-1). Returns the
    /// page plus the total matching-row count so the caller can compute
    /// total pages.
    /// </summary>
    public async Task<(List<Node> Items, int Total)> GetNodesAsync(Node.NodeStatus? status, int skip, int take)
    {
        IQueryable<Node> query = _dbContext.Nodes;
        if (status.HasValue)
            query = query.Where(n => n.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(n => n.LastHeartbeatAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return (items, total);
    }

    /// <summary>
    /// Get a specific node by ID
    /// </summary>
    public async Task<Node?> GetNodeByIdAsync(Guid nodeId)
    {
        return await _dbContext.Nodes.FindAsync(nodeId);
    }

    /// <summary>
    /// Mark offline nodes based on heartbeat timeout
    /// </summary>
    public async Task MarkOfflineNodesAsync()
    {
        var cutoffTime = DateTime.UtcNow.AddSeconds(-_heartbeatTimeoutSeconds);
        var offlineNodes = await _dbContext.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online && n.LastHeartbeatAt < cutoffTime)
            .ToListAsync();

        if (offlineNodes.Count != 0)
        {
            foreach (var node in offlineNodes)
            {
                node.Status = Node.NodeStatus.Offline;
                _logger.LogWarning("Node marked offline due to missing heartbeat: {NodeId}", node.Id);
            }

            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Delete a node
    /// </summary>
    public async Task DeleteNodeAsync(Guid nodeId)
    {
        var node = await _dbContext.Nodes.FindAsync(nodeId);
        if (node != null)
        {
            _dbContext.Nodes.Remove(node);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Node deleted: {NodeId}", nodeId);
        }
    }

    /// <summary>
    /// Read this node's overlay tag set. Returned to the Node on every
    /// heartbeat response (D6 / P2-5).
    /// </summary>
    public async Task<Dictionary<string, string>> GetOverlayTagsAsync(Guid nodeId)
    {
        return await _dbContext.NodeOverlayTags
            .Where(t => t.NodeId == nodeId)
            .ToDictionaryAsync(t => t.Key, t => t.Value);
    }

    /// <summary>
    /// Queue an env op (Set or Delete) for the next heartbeat to deliver.
    /// Multiple ops for the same key are kept in submission order — the Node
    /// applies them as a sequence. P1-5a.
    /// </summary>
    public async Task<NodeEnvOp> EnqueueEnvOpAsync(Guid nodeId, NodeEnvOp.EnvOpKind op, string key, string? value)
    {
        if (!await _dbContext.Nodes.AnyAsync(n => n.Id == nodeId))
            throw new InvalidOperationException($"Node {nodeId} not found");

        var row = new NodeEnvOp
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Op = op,
            Key = key,
            Value = op == NodeEnvOp.EnvOpKind.Set ? value : null,
            CreatedAt = DateTime.UtcNow,
        };
        _dbContext.NodeEnvOps.Add(row);
        await _dbContext.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// Drain a batch of pending env ops for delivery in a heartbeat response.
    /// Marks each as sent so they're not re-fired immediately, but keeps them
    /// in the table until the Node acks via <see cref="AckEnvOpsAsync"/>.
    /// Re-sent if not acked within the grace window.
    /// </summary>
    public async Task<List<NodeEnvOp>> DrainEnvOpsForHeartbeatAsync(Guid nodeId)
    {
        var resendCutoff = DateTime.UtcNow.AddSeconds(-30);
        var pending = await _dbContext.NodeEnvOps
            .Where(o => o.NodeId == nodeId
                        && (o.LastSentAt == null || o.LastSentAt < resendCutoff))
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .ToListAsync();

        if (pending.Count == 0) return pending;

        var now = DateTime.UtcNow;
        foreach (var op in pending) op.LastSentAt = now;
        await _dbContext.SaveChangesAsync();
        return pending;
    }

    /// <summary>
    /// Delete acked env ops (the Node confirmed they were applied).
    /// </summary>
    public async Task AckEnvOpsAsync(Guid nodeId, IReadOnlyList<Guid> opIds)
    {
        if (opIds.Count == 0) return;
        var toDelete = await _dbContext.NodeEnvOps
            .Where(o => o.NodeId == nodeId && opIds.Contains(o.Id))
            .ToListAsync();
        if (toDelete.Count == 0) return;
        _dbContext.NodeEnvOps.RemoveRange(toDelete);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// List the keys currently pending delivery to a Node. Does not include
    /// keys the Node has already applied — operators wanting authoritative
    /// state must read the Node directly until heartbeat-reported key sets
    /// land.
    /// </summary>
    public async Task<List<string>> ListPendingEnvKeysAsync(Guid nodeId)
    {
        return await _dbContext.NodeEnvOps
            .Where(o => o.NodeId == nodeId)
            .Select(o => o.Key)
            .Distinct()
            .ToListAsync();
    }

    /// <summary>
    /// Compute which <c>tasks.tagged.&lt;hash&gt;</c> queue names a Node
    /// should be subscribed to (P0-3a). A route applies to a Node iff the
    /// Node's effective tags (static merged with overlay) are a superset of
    /// the route's selector.
    /// </summary>
    public async Task<List<string>> GetTaggedSubscriptionsAsync(Guid nodeId)
    {
        var node = await _dbContext.Nodes.FindAsync(nodeId);
        if (node is null) return new List<string>();

        var effective = ComposeEffectiveTags(node.StaticTagsJson,
            await _dbContext.NodeOverlayTags.Where(t => t.NodeId == nodeId).ToListAsync());
        if (effective.Count == 0) return new List<string>();

        var routes = await _dbContext.TaggedRoutes.ToListAsync();
        var subscriptions = new List<string>();
        foreach (var route in routes)
        {
            var selector = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(route.SelectorJson);
            if (selector is null || selector.Count == 0) continue;
            if (selector.All(req => effective.TryGetValue(req.Key, out var v) && v == req.Value))
                subscriptions.Add($"tasks.tagged.{route.Hash}");
        }
        return subscriptions;
    }

    private static Dictionary<string, string> ComposeEffectiveTags(string? staticTagsJson, List<NodeOverlayTag> overlay)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in overlay) merged[t.Key] = t.Value;          // overlay first
        if (!string.IsNullOrWhiteSpace(staticTagsJson))
        {
            try
            {
                var stat = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(staticTagsJson);
                if (stat is not null)
                    foreach (var (k, v) in stat) merged[k] = v;       // static wins (D6)
            }
            catch { /* malformed static tags — treat as empty */ }
        }
        return merged;
    }

    /// <summary>
    /// Apply add/remove operations to a node's overlay tags. Adds upsert by
    /// (NodeId, Key); removes delete by Key. Returns the merged effective
    /// overlay tag set after the changes.
    /// </summary>
    public async Task<Dictionary<string, string>> UpdateOverlayTagsAsync(
        Guid nodeId,
        IReadOnlyDictionary<string, string>? add,
        IReadOnlyList<string>? remove)
    {
        var nodeExists = await _dbContext.Nodes.AnyAsync(n => n.Id == nodeId);
        if (!nodeExists)
            throw new InvalidOperationException($"Node {nodeId} not found");

        if (remove is { Count: > 0 })
        {
            var toRemove = await _dbContext.NodeOverlayTags
                .Where(t => t.NodeId == nodeId && remove.Contains(t.Key))
                .ToListAsync();
            _dbContext.NodeOverlayTags.RemoveRange(toRemove);
        }

        if (add is { Count: > 0 })
        {
            var existing = await _dbContext.NodeOverlayTags
                .Where(t => t.NodeId == nodeId && add.Keys.Contains(t.Key))
                .ToListAsync();
            var existingByKey = existing.ToDictionary(t => t.Key);

            foreach (var (k, v) in add)
            {
                if (existingByKey.TryGetValue(k, out var row))
                {
                    row.Value = v;
                    row.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _dbContext.NodeOverlayTags.Add(new NodeOverlayTag
                    {
                        Id = Guid.NewGuid(),
                        NodeId = nodeId,
                        Key = k,
                        Value = v,
                        UpdatedAt = DateTime.UtcNow,
                    });
                }
            }
        }

        await _dbContext.SaveChangesAsync();
        return await GetOverlayTagsAsync(nodeId);
    }

    /// <summary>
    /// Generate a random name for nodeId
    /// </summary>
    /// <returns></returns>
    private static string GenerateNodeName()
    {
        var generator = new NameGenerator.Generators.GamerTagGenerator
        {
            GeneratorFlags = NameGenerator.GeneratorBase.NameTypes.Hashtag,
            Sex = NameGenerator.GeneratorBase.SexTypes.Unisex
        };

        string name = generator.Generate();
        return name;
    }
}
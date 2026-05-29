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
    /// Get all nodes with optional filtering
    /// </summary>
    public async Task<List<Node>> GetNodesAsync(Node.NodeStatus? status = null)
    {
        var query = _dbContext.Nodes.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(n => n.Status == status.Value);
        }

        return await query.OrderByDescending(n => n.LastHeartbeatAt).ToListAsync();
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
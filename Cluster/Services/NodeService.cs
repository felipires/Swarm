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
    public async Task<RequestNodeRegistrationResponse> RegisterNodeAsync(string apiKey, Guid? nodeId, Dictionary<string, string>? environmentTags = null)
    {
        _logger.LogInformation("Registering node: {NodeName}", nodeId);

        var existentNodeByName = await _dbContext.Nodes.Where(x => x.Id == nodeId).Select(x => x.Name).FirstOrDefaultAsync();

        var node = new Node
        {
            Id = nodeId ?? Guid.NewGuid(),
            Name = existentNodeByName ?? GenerateNodeName(),
            Status = Node.NodeStatus.Online,
            CreatedAt = DateTime.UtcNow,
            LastHeartbeatAt = DateTime.UtcNow,
            EnvironmentTagsJson = environmentTags != null ? JsonSerializer.Serialize(environmentTags) : null
        };

        if (existentNodeByName != null)
        {
            _dbContext.Nodes.Update(node);
        } else
        {
            _dbContext.Nodes.Add(node);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Node registered successfully: {NodeId} ({NodeName})", node.Id, node.Name);

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
    public async Task UpdateHeartbeatAsync(Guid nodeId, bool isOnline = true)
    {
        var node = await _dbContext.Nodes.FindAsync(nodeId);
        if (node == null)
        {
            _logger.LogWarning("Heartbeat received from unknown node: {NodeId}", nodeId);
            return;
        }

        var wasOffline = node.Status == Node.NodeStatus.Offline;

        node.LastHeartbeatAt = DateTime.UtcNow;
        node.Status = isOnline ? Node.NodeStatus.Online : Node.NodeStatus.Offline;

        if (isOnline && wasOffline)
        {
            _logger.LogInformation("Node came back online: {NodeId}", nodeId);
        }

        await _dbContext.SaveChangesAsync();
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
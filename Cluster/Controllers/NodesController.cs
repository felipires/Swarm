using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Attributes;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodesController : ControllerBase
{
    private readonly ILogger<NodesController> _logger;
    private readonly NodeService _nodeService;

    public NodesController(ILogger<NodesController> logger, NodeService nodeService)
    {
        _logger = logger;
        _nodeService = nodeService;
    }

    /// <summary>
    /// Get all nodes
    /// </summary>
    [HttpGet]
    // [ApiKeyRequired]
    public async Task<ActionResult<List<NodeResponse>>> GetNodes([FromQuery] Node.NodeStatus? status = null)
    {
        _logger.LogInformation("Fetching nodes with status filter: {Status}", status?.ToString() ?? "all");
        
        var nodes = await _nodeService.GetNodesAsync(status);
        
        var response = nodes.Select(n => new NodeResponse
        {
            Id = n.Id,
            Name = n.Name,
            Status = n.Status,
            LastHeartbeatAt = n.LastHeartbeatAt,
            CreatedAt = n.CreatedAt
        }).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Get a specific node by ID
    /// </summary>
    [HttpGet("{id}")]
    // [ApiKeyRequired]
    public async Task<ActionResult<NodeResponse>> GetNode(Guid id)
    {
        _logger.LogInformation("Fetching node: {NodeId}", id);
        
        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
        {
            _logger.LogWarning("Node not found: {NodeId}", id);
            return NotFound(new { error = "Node not found" });
        }

        var response = new NodeResponse
        {
            Id = node.Id,
            Name = node.Name,
            Status = node.Status,
            LastHeartbeatAt = node.LastHeartbeatAt,
            CreatedAt = node.CreatedAt
        };

        return Ok(response);
    }

    /// <summary>
    /// Delete a node   
    /// </summary>
    // [ApiKeyRequired]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteNode(Guid id)
    {
        _logger.LogInformation("Deleting node: {NodeId}", id);
        
        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
        {
            return NotFound(new { error = "Node not found" });
        }

        await _nodeService.DeleteNodeAsync(id);
        return Ok(new { message = "Node deleted successfully" });
    }

    /// <summary>
    /// Trigger offline node detection (can be called periodically via scheduler)
    /// </summary>
    [HttpPost("check-offline")]
    public async Task<ActionResult> CheckOfflineNodes()
    {
        _logger.LogInformation("Running offline node detection");
        await _nodeService.MarkOfflineNodesAsync();
        return Ok(new { message = "Offline node detection completed" });
    }
    public string Message { get; set; } = null!;
}

public class NodeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Node.NodeStatus Status { get; set; } = Node.NodeStatus.Offline;
    public string CapabilitiesJson { get; set; } = null!;
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }
}


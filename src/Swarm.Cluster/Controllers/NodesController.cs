using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;
// Disambiguate against the SDK's Swarm.Node namespace.
using Node = Swarm.Cluster.Models.Node;

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
    /// Get nodes with optional status filter (paginated, P3-1).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NodeResponse>>> GetNodes(
        [FromQuery] Node.NodeStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation("Fetching nodes with status filter: {Status}", status?.ToString() ?? "all");

        var paging = new PageRequest { Page = page, PageSize = pageSize };
        var (nodes, total) = await _nodeService.GetNodesAsync(status, paging.Skip, paging.NormalizedPageSize);

        var items = nodes.Select(n => new NodeResponse
        {
            Id = n.Id,
            Name = n.Name,
            Status = n.Status,
            LastHeartbeatAt = n.LastHeartbeatAt,
            CreatedAt = n.CreatedAt
        }).ToList();

        return Ok(new PagedResult<NodeResponse>(items, total, paging.NormalizedPage, paging.NormalizedPageSize));
    }

    /// <summary>
    /// Get a specific node by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<NodeResponse>> GetNode(Guid id)
    {
        _logger.LogInformation("Fetching node: {NodeId}", id);
        
        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
        {
            _logger.LogWarning("Node not found: {NodeId}", id);
            return NotFound(new ApiError("NODE_NOT_FOUND", $"Node {id} not found"));
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
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteNode(Guid id)
    {
        _logger.LogInformation("Deleting node: {NodeId}", id);

        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
        {
            return NotFound(new ApiError("NODE_NOT_FOUND", $"Node {id} not found"));
        }

        await _nodeService.DeleteNodeAsync(id);
        return Ok(new { message = "Node deleted successfully" });
    }

    /// <summary>
    /// Add or remove overlay tags on a node (D6 / P2-5). The Node receives the
    /// updated set on its next heartbeat. Static tags reported at registration
    /// are immutable and unaffected by this endpoint.
    /// </summary>
    [HttpPatch("{id}/tags")]
    public async Task<ActionResult<Dictionary<string, string>>> UpdateOverlayTags(Guid id, [FromBody] UpdateOverlayTagsRequest body)
    {
        // ErrorHandlingMiddleware (P3-2) maps NodeService's
        // InvalidOperationException ("Node not found") into a 400 ApiError.
        var effective = await _nodeService.UpdateOverlayTagsAsync(id, body.Add, body.Remove);
        return Ok(effective);
    }

    /// <summary>
    /// Set a task-config env secret on the Node (P1-5a). Cluster queues the
    /// op for delivery on the next heartbeat. The Node decrypts and stores
    /// locally; the Cluster does not persist the value beyond the ack window.
    /// </summary>
    [HttpPost("{id}/env")]
    public async Task<ActionResult> SetEnvSecret(Guid id, [FromBody] SetEnvSecretRequest body)
    {
        if (string.IsNullOrEmpty(body.Key))
            return BadRequest(new ApiError("INVALID_KEY", "Key is required"));

        var op = await _nodeService.EnqueueEnvOpAsync(id, NodeEnvOp.EnvOpKind.Set, body.Key, body.Value);
        return Accepted(new { opId = op.Id, key = op.Key });
    }

    /// <summary>
    /// Queue a delete for a single key on the Node (P1-5a). The Node removes
    /// the key from its local encrypted store on the next heartbeat.
    /// </summary>
    [HttpDelete("{id}/env/{key}")]
    public async Task<ActionResult> DeleteEnvSecret(Guid id, string key)
    {
        var op = await _nodeService.EnqueueEnvOpAsync(id, NodeEnvOp.EnvOpKind.Delete, key, value: null);
        return Accepted(new { opId = op.Id, key = op.Key });
    }

    /// <summary>
    /// List env keys currently pending delivery to the Node (P1-5a). Does
    /// not include keys the Node has already applied — operators wanting
    /// authoritative state must read the Node directly.
    /// </summary>
    [HttpGet("{id}/env")]
    public async Task<ActionResult<List<string>>> ListEnvSecrets(Guid id)
    {
        var keys = await _nodeService.ListPendingEnvKeysAsync(id);
        return Ok(keys);
    }
}

public record UpdateOverlayTagsRequest(
    Dictionary<string, string>? Add = null,
    List<string>? Remove = null);

public record SetEnvSecretRequest(string Key, string Value);


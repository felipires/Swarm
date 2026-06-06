using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Models;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Metrics;
using Node = Swarm.Cluster.Models.Node;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodesController : ControllerBase
{
    private readonly ILogger<NodesController> _logger;
    private readonly NodeService _nodeService;
    private readonly NodeMetricsStore _metricsStore;

    public NodesController(ILogger<NodesController> logger, NodeService nodeService, NodeMetricsStore metricsStore)
    {
        _logger = logger;
        _nodeService = nodeService;
        _metricsStore = metricsStore;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<NodeResponse>>> GetNodes(
        [FromQuery] Node.NodeStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation("Fetching nodes with status filter: {Status}", status?.ToString() ?? "all");

        var paging = new PageRequest { Page = page, PageSize = pageSize };
        var (nodes, total) = await _nodeService.GetNodesAsync(status, paging.Skip, paging.NormalizedPageSize);

        var nodeIds = nodes.Select(n => n.Id).ToList();
        var capsByNode = await _nodeService.GetCapabilityTaskTypesAsync(nodeIds);

        // Fan-out Redis reads in parallel — best-effort; individual failures return null.
        var metricsTasks = nodes.ToDictionary(
            n => n.Id,
            n => _metricsStore.GetLatestAsync(n.Id));
        await Task.WhenAll(metricsTasks.Values);

        var items = nodes.Select(n =>
        {
            capsByNode.TryGetValue(n.Id, out var caps);
            var snap = metricsTasks[n.Id].Result;
            return NodeResponse.From(n, caps, snap is null ? null : ToDto(snap));
        }).ToList();

        return Ok(new PagedResult<NodeResponse>(items, total, paging.NormalizedPage, paging.NormalizedPageSize));
    }

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

        var capsByNode = await _nodeService.GetCapabilityTaskTypesAsync(new[] { id });
        capsByNode.TryGetValue(id, out var caps);
        var snap = await _metricsStore.GetLatestAsync(id);

        return Ok(NodeResponse.From(node, caps, snap is null ? null : ToDto(snap)));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteNode(Guid id)
    {
        _logger.LogInformation("Deleting node: {NodeId}", id);

        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
            return NotFound(new ApiError("NODE_NOT_FOUND", $"Node {id} not found"));

        await _nodeService.DeleteNodeAsync(id);
        return Ok(new { message = "Node deleted successfully" });
    }

    [HttpPatch("{id}/tags")]
    public async Task<ActionResult<Dictionary<string, string>>> UpdateOverlayTags(Guid id, [FromBody] UpdateOverlayTagsRequest body)
    {
        var effective = await _nodeService.UpdateOverlayTagsAsync(id, body.Add, body.Remove);
        return Ok(effective);
    }

    [HttpPost("{id}/env")]
    public async Task<ActionResult> SetEnvSecret(Guid id, [FromBody] SetEnvSecretRequest body)
    {
        if (string.IsNullOrEmpty(body.Key))
            return BadRequest(new ApiError("INVALID_KEY", "Key is required"));

        var op = await _nodeService.EnqueueEnvOpAsync(id, NodeEnvOp.EnvOpKind.Set, body.Key, body.Value);
        return Accepted(new { opId = op.Id, key = op.Key });
    }

    [HttpDelete("{id}/env/{key}")]
    public async Task<ActionResult> DeleteEnvSecret(Guid id, string key)
    {
        var op = await _nodeService.EnqueueEnvOpAsync(id, NodeEnvOp.EnvOpKind.Delete, key, value: null);
        return Accepted(new { opId = op.Id, key = op.Key });
    }

    [HttpGet("{id}/env")]
    public async Task<ActionResult<List<string>>> ListEnvSecrets(Guid id)
    {
        var keys = await _nodeService.ListPendingEnvKeysAsync(id);
        return Ok(keys);
    }

    /// <summary>
    /// P5-1: recent metrics history for a node (newest-first, up to 120 samples).
    /// Returns empty list if Redis is unavailable or no history recorded yet.
    /// </summary>
    [HttpGet("{id}/metrics")]
    public async Task<ActionResult<List<NodeMetricsDto>>> GetMetricsHistory(Guid id)
    {
        var node = await _nodeService.GetNodeByIdAsync(id);
        if (node == null)
            return NotFound(new ApiError("NODE_NOT_FOUND", $"Node {id} not found"));

        var history = await _metricsStore.GetHistoryAsync(id);
        return Ok(history.Select(ToDto).ToList());
    }

    private static NodeMetricsDto ToDto(NodeMetricsSnapshot s) => new()
    {
        RecordedAt = s.RecordedAt,
        CpuPercent = s.CpuPercent,
        MemoryUsedBytes = s.MemoryUsedBytes,
        MemoryAvailableBytes = s.MemoryAvailableBytes,
        InFlightTasks = s.InFlightTasks,
        UptimeSeconds = s.UptimeSeconds,
        Health = s.Health,
    };
}

public record UpdateOverlayTagsRequest(
    Dictionary<string, string>? Add = null,
    List<string>? Remove = null);

public record SetEnvSecretRequest(string Key, string Value);

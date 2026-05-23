using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Services;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController : ControllerBase
{
    private readonly LogConsumerService _logConsumerService;
    private readonly ILogger<LogsController> _logger;

    public LogsController(LogConsumerService logConsumerService, ILogger<LogsController> logger)
    {
        _logConsumerService = logConsumerService;
        _logger = logger;
    }

    /// <summary>
    /// Streams the buffered logs for a specific node using Server-Sent Events.
    /// </summary>
    /// <param name="nodeId">The node ID to stream logs for</param>
    [HttpGet("stream/{nodeId}")]
    public async Task StreamLogs(Guid nodeId)
    {
        Response.Headers.Add("Content-Type", "text/event-stream");
        Response.Headers.Add("Cache-Control", "no-cache");
        Response.Headers.Add("Connection", "keep-alive");

        try
        {
            var bufferSize = _logConsumerService.GetBufferSizeForNode(nodeId);

            if (bufferSize == 0)
            {
                await Response.WriteAsync("data: {\"message\": \"No logs in buffer for this node\"}\n\n");
            }
            else
            {
                await Response.WriteAsync($"data: {{\"bufferSize\": {bufferSize}}}\n\n");
                await Response.Body.FlushAsync();
            }

            _logger.LogInformation("Buffer status for node {NodeId}: {BufferSize} logs", nodeId, bufferSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming logs for node {NodeId}", nodeId);
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = ex.Message })}\n\n");
        }
    }

    /// <summary>
    /// Gets the current size of the log buffer for a specific node.
    /// </summary>
    /// <param name="nodeId">The node ID to check buffer size for</param>
    [HttpGet("buffer-status/{nodeId}")]
    public IActionResult GetBufferStatus(Guid nodeId)
    {
        try
        {
            var bufferSize = _logConsumerService.GetBufferSizeForNode(nodeId);
            return Ok(new { nodeId, bufferSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting buffer status for node {NodeId}", nodeId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

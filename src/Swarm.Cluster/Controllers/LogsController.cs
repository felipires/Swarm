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
    public async Task StreamLogs(Guid nodeId, CancellationToken cancellationToken)
    {
        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        // Disable buffering so each event is flushed to the client immediately
        var bufferingFeature = Response.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bufferingFeature?.DisableBuffering();

        // Replay recent buffered logs so the UI isn't blank on connect
        foreach (var log in _logConsumerService.GetRecentLogsForNode(nodeId))
            await WriteLogAsync(log, cancellationToken);

        var channel = _logConsumerService.Subscribe(nodeId);
        try
        {
            _logger.LogInformation("SSE client connected for node {NodeId}", nodeId);

            // Initial ping so the browser fires onopen immediately
            await Response.WriteAsync(": connected\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            await foreach (var log in channel.Reader.ReadAllAsync(cancellationToken))
                await WriteLogAsync(log, cancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _logConsumerService.Unsubscribe(nodeId, channel);
            _logger.LogInformation("SSE client disconnected for node {NodeId}", nodeId);
        }
    }

    private async Task WriteLogAsync(Swarm.Cluster.Models.Log log, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = log.Id,
            nodeId = log.NodeId,
            level = log.Level,
            message = log.Message ?? log.MessageTemplate,
            timestamp = log.Timestamp,
            exception = log.Exception
        });
        await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
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

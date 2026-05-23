using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using System.Text.RegularExpressions;

namespace Swarm.Cluster.Services;

/// <summary>
/// Consumes Serilog logs from RabbitMQ, buffers them, and performs bulk inserts into the database.
/// Maintains a buffer of the latest logs per node for quick access.
/// </summary>
public partial class LogConsumerService : BackgroundService
{
    private readonly ILogger<LogConsumerService> _logger;
    private readonly IConnection _rabbitConnection;
    private readonly ClusterDbContext _dbContext;
    private IModel? _channel;
    private CancellationToken _stoppingToken;

    private readonly object _bufferLock = new object();
    private readonly Dictionary<Guid, List<Log>> _logBuffer = new();
    private readonly Dictionary<Guid, List<Log>> _latestLogsBuffer = new();

    // SSE subscribers: nodeId → list of channels waiting for new logs
    private readonly Dictionary<Guid, List<System.Threading.Channels.Channel<Log>>> _subscribers = new();

    private const string LogQueueName = "logs";
    private const int BufferFlushIntervalMs = 5000;
    private const int MaxBufferSizePerNode = 1000;
    private const int LatestLogsPerNodeToKeep = 100;

    public LogConsumerService(ClusterDbContext dbContext, ILogger<LogConsumerService> logger, IConnection rabbitConnection)
    {
        _dbContext = dbContext;
        _logger = logger;
        _rabbitConnection = rabbitConnection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        try
        {
            _channel = _rabbitConnection.CreateModel();

            _channel.QueueDeclare(
                queue: LogQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.Received += OnLogReceived;

            _channel.BasicConsume(
                queue: LogQueueName,
                autoAck: false,
                consumerTag: "cluster-log-consumer",
                consumer: consumer);

            _logger.LogInformation("Log consumer started, listening on queue '{QueueName}'", LogQueueName);

            _ = FlushBufferPeriodically(stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Log consumer is stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in log consumer");
            throw;
        }
    }

    private Task OnLogReceived(object model, BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = System.Text.Encoding.UTF8.GetString(body);
            
            var logEntry = ParseLogMessage(message, ea.BasicProperties);
            _logger.LogDebug("Received log from {nodeId}", logEntry?.NodeId);
            
            if (logEntry != null)
            {
                lock (_bufferLock)
                {
                    if (!_logBuffer.TryGetValue(logEntry.NodeId, out List<Log>? value))
                    {
                        value = new List<Log>();
                        _logBuffer[logEntry.NodeId] = value;
                        _latestLogsBuffer[logEntry.NodeId] = new List<Log>();
                    }

                    value.Add(logEntry);
                    _latestLogsBuffer[logEntry.NodeId].Add(logEntry);

                    if (_latestLogsBuffer[logEntry.NodeId].Count > LatestLogsPerNodeToKeep)
                        _latestLogsBuffer[logEntry.NodeId].RemoveAt(0);

                    if (value.Count >= MaxBufferSizePerNode)
                        _ = FlushLogsForNodeAsync(logEntry.NodeId, _stoppingToken);

                    // Push to SSE subscribers
                    if (_subscribers.TryGetValue(logEntry.NodeId, out var subs))
                        foreach (var sub in subs)
                            sub.Writer.TryWrite(logEntry);
                }

                _channel!.BasicAck(ea.DeliveryTag, false);
            }
            else
            {
                _logger.LogWarning("Failed to parse log message from RabbitMQ");
                _channel!.BasicNack(ea.DeliveryTag, false, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing log message from RabbitMQ");
            try { _channel!.BasicNack(ea.DeliveryTag, false, true); } catch { }
        }

        return Task.CompletedTask;
    }

    private Log? ParseLogMessage(string message, IBasicProperties properties)
    {
        try
        {
            _logger.LogDebug("Raw RabbitMQ log message: {Raw}", message[..Math.Min(500, message.Length)]);

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Serilog compact JSON format uses @t, @mt, @l, @x; enriched properties are top-level
            bool isCompact = root.TryGetProperty("@mt", out _);

            Guid nodeId = Guid.Empty;
            var nodeIdStr = isCompact
                ? GetJsonPropertyAsString(root, "NodeId")
                : (GetJsonPropertyAsString(root, "NodeId") ??
                   (root.TryGetProperty("Properties", out var props2) ? GetJsonPropertyAsString(props2, "NodeId") : null));

            if (!Guid.TryParse(nodeIdStr, out nodeId))
            {
                if (properties?.Headers?.TryGetValue("X-Node-Id", out var nodeIdObj) == true &&
                    nodeIdObj is byte[] nodeIdBytes &&
                    Guid.TryParse(System.Text.Encoding.UTF8.GetString(nodeIdBytes), out nodeId))
                {
                    // found in AMQP headers
                }
                else
                {
                    _logger.LogWarning("No NodeId found in log message, raw: {Raw}", message[..Math.Min(200, message.Length)]);
                    return null;
                }
            }

            string messageTemplate;
            string renderedMessage;
            string? level;
            string? exception;
            DateTime timestamp;

            if (isCompact)
            {
                messageTemplate = GetJsonPropertyAsString(root, "@mt") ?? "";
                renderedMessage = RenderCompactTemplate(root, messageTemplate);
                level = CompactLevelToFull(GetJsonPropertyAsString(root, "@l"));
                exception = GetJsonPropertyAsString(root, "@x");
                timestamp = GetJsonPropertyAsDateTime(root, "@t");
            }
            else
            {
                messageTemplate = GetJsonPropertyAsString(root, "MessageTemplate") ?? GetJsonPropertyAsString(root, "Message") ?? "";
                renderedMessage = GetJsonPropertyAsString(root, "RenderedMessage") ?? messageTemplate;
                level = GetJsonPropertyAsString(root, "Level");
                exception = GetJsonPropertyAsString(root, "Exception");
                timestamp = GetJsonPropertyAsDateTime(root, "Timestamp");
            }

            var log = new Log
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Level = level ?? "Information",
                MessageTemplate = messageTemplate,
                Message = renderedMessage,
                Exception = exception,
                Timestamp = timestamp,
                CreatedAt = DateTime.UtcNow,
                Properties = ExtractProperties(root)
            };

            return log;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse log message JSON");
            return null;
        }
    }

    private static string RenderCompactTemplate(JsonElement root, string template)
    {
        return MyRegex().Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (root.TryGetProperty(key, out var val))
                return val.ValueKind == JsonValueKind.String ? val.GetString()! : val.GetRawText();
            return m.Value;
        });
    }

    private static string CompactLevelToFull(string? level) => level switch
    {
        "V" or "Verbose" => "Verbose",
        "D" or "Debug"   => "Debug",
        "W" or "Warning" => "Warning",
        "E" or "Error"   => "Error",
        "F" or "Fatal"   => "Fatal",
        _                => "Information",
    };

    private static string? GetJsonPropertyAsString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            return property.GetString();
        }
        return null;
    }

    private static DateTime GetJsonPropertyAsDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (DateTime.TryParse(property.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        }
        return DateTime.UtcNow;
    }

    private static string? ExtractProperties(JsonElement root)
    {
        try
        {
            if (root.TryGetProperty("Properties", out var props))
            {
                return props.GetRawText();
            }
            return null;
        }
        catch { }
        return null;
    }

    private async Task FlushBufferPeriodically(CancellationToken cancellationToken)
    {
        try
        {
           while (!cancellationToken.IsCancellationRequested) 
            {
                try
                {
                    await Task.Delay(BufferFlushIntervalMs, cancellationToken);

                    lock (_bufferLock)
                    {
                        foreach (var nodeId in _logBuffer.Keys.ToList())
                        {
                            if (_logBuffer[nodeId].Count > 0)
                            {
                                _ = FlushLogsForNodeAsync(nodeId, cancellationToken);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Buffer flush task cancelled");
        }
    }

    private async Task FlushLogsForNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        List<Log> logsToFlush;
        lock (_bufferLock)
        {
            if (!_logBuffer.TryGetValue(nodeId, out List<Log>? value) || value.Count == 0)
                return;

            logsToFlush = [.. value];
            value.Clear();
        }

        try
        {
            var rows = string.Join(",", logsToFlush.Select(_ => "(gen_random_uuid(),@p{0},@p{1},@p{2},@p{3},@p{4},@p{5},@p{6},@p{7})"));
            var parameters = new List<Npgsql.NpgsqlParameter>();
            var valueClauses = new System.Text.StringBuilder();

            for (int i = 0; i < logsToFlush.Count; i++)
            {
                var log = logsToFlush[i];
                int b = i * 7;
                if (i > 0) valueClauses.Append(',');
                valueClauses.Append($"(@n{b},@l{b},@mt{b},@m{b},@ex{b},@pr{b},@ts{b},@ca{b})");
                parameters.Add(new Npgsql.NpgsqlParameter($"n{b}", log.NodeId));
                parameters.Add(new Npgsql.NpgsqlParameter($"l{b}", log.Level));
                parameters.Add(new Npgsql.NpgsqlParameter($"mt{b}", log.MessageTemplate));
                parameters.Add(new Npgsql.NpgsqlParameter($"m{b}", (object?)log.Message ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter($"ex{b}", (object?)log.Exception ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter($"pr{b}", (object?)log.Properties ?? DBNull.Value));
                parameters.Add(new Npgsql.NpgsqlParameter($"ts{b}", log.Timestamp));
                parameters.Add(new Npgsql.NpgsqlParameter($"ca{b}", log.CreatedAt));
            }

            var sql = $"INSERT INTO \"Logs\" (\"Id\",\"NodeId\",\"Level\",\"MessageTemplate\",\"Message\",\"Exception\",\"Properties\",\"Timestamp\",\"CreatedAt\") SELECT gen_random_uuid(), * FROM (VALUES {valueClauses}) AS v(\"NodeId\",\"Level\",\"MessageTemplate\",\"Message\",\"Exception\",\"Properties\",\"Timestamp\",\"CreatedAt\")";
            await _dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

            _logger.LogInformation("Flushed {LogCount} logs for node {NodeId} to database", logsToFlush.Count, nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing logs for node {NodeId}", nodeId);

            // Put logs back in the buffer so they are not lost
            lock (_bufferLock)
            {
                if (!_logBuffer.ContainsKey(nodeId))
                    _logBuffer[nodeId] = new List<Log>();
                _logBuffer[nodeId].InsertRange(0, logsToFlush);
            }
        }
    }

    public int GetBufferSizeForNode(Guid nodeId)
    {
        lock (_bufferLock)
        {
            return _logBuffer.TryGetValue(nodeId, out List<Log>? value) ? value.Count : 0;
        }
    }

    public List<Log> GetRecentLogsForNode(Guid nodeId)
    {
        lock (_bufferLock)
        {
            return _latestLogsBuffer.TryGetValue(nodeId, out var logs)
                ? [.. logs]
                : [];
        }
    }

    public System.Threading.Channels.Channel<Log> Subscribe(Guid nodeId)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<Log>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        lock (_bufferLock)
        {
            if (!_subscribers.ContainsKey(nodeId))
                _subscribers[nodeId] = new List<System.Threading.Channels.Channel<Log>>();
            _subscribers[nodeId].Add(channel);
        }
        return channel;
    }

    public void Unsubscribe(Guid nodeId, System.Threading.Channels.Channel<Log> channel)
    {
        lock (_bufferLock)
        {
            if (_subscribers.TryGetValue(nodeId, out var subs))
                subs.Remove(channel);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping log consumer and flushing remaining logs");
        
        // Flush remaining logs
        lock (_bufferLock)
        {
            var nodeIds = _logBuffer.Keys.ToList();
            foreach (var nodeId in nodeIds)
            {
                if (_logBuffer[nodeId].Count > 0)
                {
                    _ = FlushLogsForNodeAsync(nodeId, cancellationToken);
                }
            }
        }

        await Task.Delay(1000, cancellationToken);

        _channel?.Dispose();

        await base.StopAsync(cancellationToken);
    }

    [GeneratedRegexAttribute(@"\{(\w+)(?::[^}]*)?\}", RegexOptions.Compiled)]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}

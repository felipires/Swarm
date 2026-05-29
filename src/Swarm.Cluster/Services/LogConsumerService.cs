using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Consumes Serilog logs from RabbitMQ, buffers them per-node, and bulk-inserts
/// into Postgres on a periodic flush or when the buffer fills. Uses a singleton
/// <see cref="NpgsqlDataSource"/> for its DB writes — never EF Core — so it can
/// safely live for the application lifetime.
/// </summary>
public partial class LogConsumerService : BackgroundService
{
    private readonly ILogger<LogConsumerService> _logger;
    private readonly IConnection _rabbitConnection;
    private readonly NpgsqlDataSource _db;
    private IModel? _channel;
    private CancellationToken _stoppingToken;

    private readonly object _bufferLock = new();
    private readonly Dictionary<Guid, List<Log>> _logBuffer = new();
    private readonly Dictionary<Guid, List<Log>> _latestLogsBuffer = new();

    // Per-node flush serialization (P2-2). One semaphore per node — a flush
    // in progress for node X never blocks node Y.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _flushLocks = new();

    // Consecutive flush-failure counter per node, used for exponential backoff
    // on the buffer-cap recovery path (P2-3).
    private readonly ConcurrentDictionary<Guid, int> _flushFailures = new();

    // SSE subscribers: nodeId → list of channels waiting for new logs
    private readonly Dictionary<Guid, List<System.Threading.Channels.Channel<Log>>> _subscribers = new();

    private const string LogQueueName = "logs";
    private const int BufferFlushIntervalMs = 5000;
    private const int MaxBufferSizePerNode = 1000;
    private const int LatestLogsPerNodeToKeep = 100;
    // Hard cap on per-node buffer once flushes are persistently failing.
    // Older entries get dropped (FIFO) rather than growing memory unbounded (P2-3).
    private const int MaxBufferTotalCap = 5000;

    public LogConsumerService(NpgsqlDataSource db, ILogger<LogConsumerService> logger, IConnection rabbitConnection)
    {
        _db = db;
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

                    List<Guid> nodesWithLogs;
                    lock (_bufferLock)
                    {
                        nodesWithLogs = _logBuffer
                            .Where(kv => kv.Value.Count > 0)
                            .Select(kv => kv.Key)
                            .ToList();
                    }

                    foreach (var nodeId in nodesWithLogs)
                        _ = FlushLogsForNodeAsync(nodeId, cancellationToken);
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
        // P2-2: at most one flush in flight per node. WaitAsync(0) returns
        // immediately — if a flush is already running, a fresh trigger is a no-op
        // and the next periodic tick (or buffer-full event) will retry.
        var sem = _flushLocks.GetOrAdd(nodeId, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0, cancellationToken)) return;

        try
        {
            List<Log> logsToFlush;
            lock (_bufferLock)
            {
                if (!_logBuffer.TryGetValue(nodeId, out var value) || value.Count == 0)
                    return;

                logsToFlush = [.. value];
                value.Clear();
            }

            try
            {
                await BulkInsertLogsAsync(logsToFlush, cancellationToken);
                _flushFailures.TryRemove(nodeId, out _);
                _logger.LogInformation("Flushed {LogCount} logs for node {NodeId} to database", logsToFlush.Count, nodeId);
            }
            catch (Exception ex)
            {
                var failures = _flushFailures.AddOrUpdate(nodeId, 1, (_, c) => c + 1);
                _logger.LogError(ex,
                    "Error flushing logs for node {NodeId} (consecutive failures: {Failures})",
                    nodeId, failures);

                // P2-3: re-insert head-first, then truncate the oldest entries so
                // the buffer cannot grow without bound while flushes keep failing.
                lock (_bufferLock)
                {
                    if (!_logBuffer.TryGetValue(nodeId, out var current))
                    {
                        current = new List<Log>();
                        _logBuffer[nodeId] = current;
                    }

                    var combined = new List<Log>(logsToFlush.Count + current.Count);
                    combined.AddRange(logsToFlush);
                    combined.AddRange(current);

                    if (combined.Count > MaxBufferTotalCap)
                    {
                        var dropped = combined.Count - MaxBufferTotalCap;
                        combined.RemoveRange(0, dropped);
                        _logger.LogWarning(
                            "Log buffer for node {NodeId} hit cap; dropped {Dropped} oldest entries",
                            nodeId, dropped);
                    }

                    _logBuffer[nodeId] = combined;
                }
            }
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task BulkInsertLogsAsync(IReadOnlyList<Log> logs, CancellationToken cancellationToken)
    {
        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var valueClauses = new System.Text.StringBuilder();
        for (int i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            int b = i * 7;
            if (i > 0) valueClauses.Append(',');
            valueClauses.Append($"(@n{b},@l{b},@mt{b},@m{b},@ex{b},@pr{b},@ts{b},@ca{b})");

            cmd.Parameters.Add(new NpgsqlParameter($"n{b}", log.NodeId));
            cmd.Parameters.Add(new NpgsqlParameter($"l{b}", log.Level));
            cmd.Parameters.Add(new NpgsqlParameter($"mt{b}", log.MessageTemplate));
            cmd.Parameters.Add(new NpgsqlParameter($"m{b}", (object?)log.Message ?? DBNull.Value));
            cmd.Parameters.Add(new NpgsqlParameter($"ex{b}", (object?)log.Exception ?? DBNull.Value));
            cmd.Parameters.Add(new NpgsqlParameter($"pr{b}", (object?)log.Properties ?? DBNull.Value));
            cmd.Parameters.Add(new NpgsqlParameter($"ts{b}", log.Timestamp));
            cmd.Parameters.Add(new NpgsqlParameter($"ca{b}", log.CreatedAt));
        }

        cmd.CommandText =
            "INSERT INTO \"Logs\" (\"Id\",\"NodeId\",\"Level\",\"MessageTemplate\",\"Message\",\"Exception\",\"Properties\",\"Timestamp\",\"CreatedAt\") " +
            "SELECT gen_random_uuid(), * FROM (VALUES " + valueClauses + ") " +
            "AS v(\"NodeId\",\"Level\",\"MessageTemplate\",\"Message\",\"Exception\",\"Properties\",\"Timestamp\",\"CreatedAt\")";

        await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        List<Guid> nodeIds;
        lock (_bufferLock)
        {
            nodeIds = _logBuffer
                .Where(kv => kv.Value.Count > 0)
                .Select(kv => kv.Key)
                .ToList();
        }

        var pending = nodeIds.Select(id => FlushLogsForNodeAsync(id, cancellationToken));
        await Task.WhenAll(pending);

        _channel?.Dispose();

        await base.StopAsync(cancellationToken);
    }

    [GeneratedRegex(@"\{(\w+)(?::[^}]*)?\}", RegexOptions.Compiled)]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}

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
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Consumes Serilog logs from RabbitMQ, buffers them, and performs bulk inserts into the database.
/// Maintains a buffer of the latest logs per node for quick access.
/// </summary>
public class LogConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LogConsumerService> _logger;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IModel? _channel;
    private CancellationToken _stoppingToken;

    private readonly object _bufferLock = new object();
    private readonly Dictionary<Guid, List<Log>> _logBuffer = new();
    private readonly Dictionary<Guid, List<Log>> _latestLogsBuffer = new();

    private const string LogQueueName = "logs";
    private const int BufferFlushIntervalMs = 5000;
    private const int MaxBufferSizePerNode = 1000;
    private const int LatestLogsPerNodeToKeep = 100;

    public LogConsumerService(IServiceProvider serviceProvider, ILogger<LogConsumerService> logger, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        try
        {
            var rabbitMqConfig = _configuration.GetSection("RabbitMQ");
            var connectionFactory = new ConnectionFactory
            {
                HostName = rabbitMqConfig["Hostname"] ?? "localhost",
                Port = ushort.Parse(rabbitMqConfig["Port"] ?? "5672"),
                UserName = rabbitMqConfig["Username"] ?? "guest",
                Password = rabbitMqConfig["Password"] ?? "guest",
                VirtualHost = rabbitMqConfig["VirtualHost"] ?? "/",
                DispatchConsumersAsync = true
            };

            _connection = connectionFactory.CreateConnection();
            _channel = _connection.CreateModel();

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

    private async Task OnLogReceived(object model, BasicDeliverEventArgs ea)
    {
        try
        {
            var body = ea.Body.ToArray();
            var message = System.Text.Encoding.UTF8.GetString(body);
            
            var logEntry = ParseLogMessage(message, ea.BasicProperties);
            
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

                    // Keep only the latest logs in the latest buffer
                    if (_latestLogsBuffer[logEntry.NodeId].Count > LatestLogsPerNodeToKeep)
                    {
                        _latestLogsBuffer[logEntry.NodeId].RemoveAt(0);
                    }

                    // Flush if buffer exceeds max size
                    if (value.Count >= MaxBufferSizePerNode)
                    {
                        _ = FlushLogsForNodeAsync(logEntry.NodeId, _stoppingToken);
                    }
                }

                _channel.BasicAck(ea.DeliveryTag, false);
            }
            else
            {
                _logger.LogWarning("Failed to parse log message from RabbitMQ");
                _channel.BasicNack(ea.DeliveryTag, false, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing log message from RabbitMQ");
            try
            {
                _channel.BasicNack(ea.DeliveryTag, false, true);
            }
            catch { }
        }
    }

    private Log? ParseLogMessage(string message, IBasicProperties properties)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            // Extract NodeId from properties or message
            if (!Guid.TryParse(GetJsonPropertyAsString(root, "NodeId"), out var nodeId))
            {
                // Try from headers
                if (properties?.Headers?.TryGetValue("X-Node-Id", out var nodeIdObj) == true)
                {
                    var nodeIdBytes = nodeIdObj as byte[];
                    if (nodeIdBytes != null && Guid.TryParse(System.Text.Encoding.UTF8.GetString(nodeIdBytes), out nodeId))
                    {
                        // Found node ID in headers
                    }
                    else
                    {
                        _logger.LogWarning("Could not parse NodeId from message headers");
                        return null;
                    }
                }
                else
                {
                    _logger.LogWarning("No NodeId found in log message");
                    return null;
                }
            }

            var log = new Log
            {
                Id = Guid.NewGuid(),
                NodeId = nodeId,
                Level = GetJsonPropertyAsString(root, "Level") ?? "Information",
                MessageTemplate = GetJsonPropertyAsString(root, "MessageTemplate") ?? GetJsonPropertyAsString(root, "Message") ?? "",
                Message = GetJsonPropertyAsString(root, "RenderedMessage"),
                Exception = GetJsonPropertyAsString(root, "Exception"),
                Timestamp = GetJsonPropertyAsDateTime(root, "Timestamp"),
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

    private string? GetJsonPropertyAsString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            return property.GetString();
        }
        return null;
    }

    private DateTime GetJsonPropertyAsDateTime(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (DateTime.TryParse(property.GetString(), out var dt))
            {
                return dt;
            }
        }
        return DateTime.UtcNow;
    }

    private string? ExtractProperties(JsonElement root)
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
            if (!_logBuffer.ContainsKey(nodeId) || _logBuffer[nodeId].Count == 0)
                return;

            logsToFlush = new List<Log>(_logBuffer[nodeId]);
            _logBuffer[nodeId].Clear();
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();
            await dbContext.Logs.AddRangeAsync(logsToFlush, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Gets the current buffer size for a specific node.
    /// </summary>
    public int GetBufferSizeForNode(Guid nodeId)
    {
        lock (_bufferLock)
        {
            return _logBuffer.ContainsKey(nodeId) ? _logBuffer[nodeId].Count : 0;
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

        await Task.Delay(1000); // Give flush tasks time to complete

        _channel?.Dispose();
        _connection?.Dispose();

        await base.StopAsync(cancellationToken);
    }
}

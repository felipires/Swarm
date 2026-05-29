using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Node.Data;
using Swarm.Node.Sdk;
using Swarm.Node.Sdk.Abstractions;
using Swarm.Node.Sdk.Wire;

namespace Swarm.Node.Services;

public class TaskExecutorService : IAsyncDisposable
{
    private readonly AppDbConnection _dbConnection;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TaskExecutorService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HandlerRegistry _registry;
    private IConnection? _rabbitConnection;
    private IChannel? _channel;

    public const string ResultQueueName = "task-results";

    public TaskExecutorService(
        AppDbConnection dbConnection,
        IConfiguration configuration,
        ILogger<TaskExecutorService> logger,
        ILoggerFactory loggerFactory,
        IEnumerable<ITaskHandler> handlers)
    {
        _dbConnection = dbConnection;
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _registry = new HandlerRegistry(handlers);

        foreach (var taskType in _registry.RegisteredTaskTypes)
            _logger.LogInformation("Registered handler {TaskType}", taskType);
    }

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        var nodeId = _configuration["NodeId"]
            ?? throw new InvalidOperationException("NodeId is not configured");

        var cfg = _configuration.GetSection("RabbitMQ");
        var factory = new ConnectionFactory
        {
            HostName = cfg["Hostname"] ?? "localhost",
            Port = cfg.GetValue<int>("Port", 5672),
            UserName = cfg["Username"] ?? "guest",
            Password = cfg["Password"] ?? "guest",
            VirtualHost = cfg["VirtualHost"] ?? "/"
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _rabbitConnection = await factory.CreateConnectionAsync(stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "RabbitMQ not reachable, retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        stoppingToken.ThrowIfCancellationRequested();

        _channel = await _rabbitConnection!.CreateChannelAsync(cancellationToken: stoppingToken);

        var queueName = $"tasks.{nodeId}";
        await _channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(queue: ResultQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, ea) => OnTaskReceived(ea, stoppingToken);

        await _channel.BasicConsumeAsync(queue: queueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        _logger.LogInformation("Task executor listening on queue '{Queue}'", queueName);
    }

    private async Task OnTaskReceived(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        TaskMessage? message = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            message = JsonSerializer.Deserialize<TaskMessage>(json);

            if (message == null)
            {
                _logger.LogWarning("Received null task message, discarding");
                await _channel!.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                return;
            }

            _logger.LogInformation("Received task {InstanceId} type={TaskType}", message.InstanceId, message.TaskType);

            var localId = await SaveLocalTaskAsync(message);
            await UpdateLocalTaskStatusAsync(localId, "running");

            var result = await DispatchAsync(message, cancellationToken);

            await UpdateLocalTaskStatusAsync(localId, result.Success ? "completed" : "failed");
            await PublishResultAsync(new TaskResultMessage
            {
                InstanceId = message.InstanceId,
                Success = result.Success,
                ResultJson = result.ResultJson,
                ErrorMessage = result.ErrorMessage,
            }, cancellationToken);

            await _channel!.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);

            _logger.LogInformation("Task {InstanceId} finished: success={Success}", message.InstanceId, result.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing task {InstanceId}", message?.InstanceId);
            await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: false, cancellationToken);

            if (message != null)
                await PublishResultAsync(new TaskResultMessage
                {
                    InstanceId = message.InstanceId,
                    Success = false,
                    ErrorMessage = ex.Message
                }, cancellationToken);
        }
    }

    internal async Task<TaskResult> DispatchAsync(TaskMessage message, CancellationToken cancellationToken)
    {
        if (!TaskTypeId.TryParse(message.TaskType, out _))
            return new TaskResult(false, ErrorMessage: $"INVALID_TASKTYPE: '{message.TaskType}'");

        if (!_registry.TryGet(message.TaskType, out var handler))
        {
            _logger.LogError("No handler registered for TaskType {TaskType}", message.TaskType);
            return new TaskResult(false, ErrorMessage: $"UNSUPPORTED_TASK_TYPE: '{message.TaskType}'");
        }

        JsonElement staticConfig;
        try
        {
            staticConfig = JsonSerializer.Deserialize<JsonElement>(message.ConfigJson);
        }
        catch (JsonException ex)
        {
            return new TaskResult(false, ErrorMessage: $"INVALID_CONFIG_JSON: {ex.Message}");
        }

        JsonElement runtimeParams = default;
        if (!string.IsNullOrEmpty(message.RuntimeParamsJson))
        {
            try
            {
                runtimeParams = JsonSerializer.Deserialize<JsonElement>(message.RuntimeParamsJson);
            }
            catch (JsonException ex)
            {
                return new TaskResult(false, ErrorMessage: $"INVALID_PARAMS_JSON: {ex.Message}");
            }
        }

        var handlerLogger = _loggerFactory.CreateLogger(handler.GetType());
        var context = new TaskContext(message, staticConfig, runtimeParams, handlerLogger, cancellationToken);
        return await handler.HandleAsync(context);
    }

    private async Task<Guid> SaveLocalTaskAsync(TaskMessage message)
    {
        var id = Guid.NewGuid();
        using var conn = new SqliteConnection(_dbConnection.GetConnectionString());
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO LocalTask (Id, ClusterTaskId, ConfigJson, Status, CreatedAt)
            VALUES ($id, $clusterTaskId, $config, 'pending', datetime('now'))
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$clusterTaskId", message.InstanceId.ToString());
        cmd.Parameters.AddWithValue("$config", message.ConfigJson);
        await cmd.ExecuteNonQueryAsync();

        return id;
    }

    private async Task UpdateLocalTaskStatusAsync(Guid localId, string status)
    {
        using var conn = new SqliteConnection(_dbConnection.GetConnectionString());
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = status is "completed" or "failed"
            ? "UPDATE LocalTask SET Status = $status, CompletedAt = datetime('now') WHERE Id = $id"
            : "UPDATE LocalTask SET Status = $status WHERE Id = $id";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$id", localId.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PublishResultAsync(TaskResultMessage result, CancellationToken cancellationToken)
    {
        if (_channel == null) return;

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json"
        };

        await _channel.BasicPublishAsync(exchange: "", routingKey: ResultQueueName, mandatory: false, basicProperties: props, body: body, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.DisposeAsync();
        if (_rabbitConnection != null) await _rabbitConnection.DisposeAsync();
    }
}

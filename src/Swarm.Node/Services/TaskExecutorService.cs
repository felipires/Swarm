using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Node.Data;
using Swarm.Node.Logging;
using Swarm.Sdk;
using Swarm.Sdk.Abstractions;
using Swarm.Sdk.ValueResolution;
using Swarm.Sdk.Wire;
using Swarm.Node.ValueResolution;
using EnvStoreResolver = Swarm.Node.ValueResolution.EnvStoreResolver;

namespace Swarm.Node.Services;

public class TaskExecutorService : IAsyncDisposable
{
    private readonly AppDbConnection _dbConnection;
    private readonly EnvSecretsStore _envSecrets;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TaskExecutorService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HandlerRegistry _registry;
    private IConnection? _rabbitConnection;
    private IChannel? _channel;
    private AsyncEventingBasicConsumer? _consumer;
    // P0-3a: tagged-queue subscriptions the Node currently holds, queueName → RabbitMQ consumer tag.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _taggedConsumers = new();
    private readonly SemaphoreSlim _subscriptionGate = new(1, 1);

    public const string ResultQueueName = "task-results";
    public const string ClaimQueueName = "task-claims";
    public static string SharedQueueName(string taskType) => $"tasks.shared.{taskType}";

    public TaskExecutorService(
        AppDbConnection dbConnection,
        EnvSecretsStore envSecrets,
        IConfiguration configuration,
        ILogger<TaskExecutorService> logger,
        ILoggerFactory loggerFactory,
        IEnumerable<ITaskHandler> handlers)
    {
        _dbConnection = dbConnection;
        _envSecrets = envSecrets;
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

        // task-results and task-claims are owned by the Cluster (DLX args per
        // P0-5 / P0-3a). We only publish to them, so we don't declare them
        // here — a redeclare with different args would fail PRECONDITION.
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        _consumer = new AsyncEventingBasicConsumer(_channel);
        _consumer.ReceivedAsync += (_, ea) => OnTaskReceived(ea, stoppingToken);

        // Always consume the per-node queue (SpecificNode / AllOnlineNodes).
        var perNodeQueue = $"tasks.{nodeId}";
        await _channel.QueueDeclareAsync(queue: perNodeQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.BasicConsumeAsync(queue: perNodeQueue, autoAck: false, consumer: _consumer, cancellationToken: stoppingToken);
        _logger.LogInformation("Subscribed to per-node queue '{Queue}'", perNodeQueue);

        // P0-3a: also consume one shared queue per advertised TaskType so
        // AnyOnlineNode dispatches load-balance across all online consumers.
        foreach (var taskType in _registry.RegisteredTaskTypes)
        {
            var shared = SharedQueueName(taskType);
            await _channel.QueueDeclareAsync(queue: shared, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await _channel.BasicConsumeAsync(queue: shared, autoAck: false, consumer: _consumer, cancellationToken: stoppingToken);
            _logger.LogInformation("Subscribed to shared queue '{Queue}'", shared);
        }
    }

    /// <summary>
    /// Reconcile the Node's tagged-queue subscriptions against the list the
    /// Cluster pushed in the latest heartbeat (P0-3a). Subscribes to any new
    /// queues, cancels any that dropped out.
    /// </summary>
    public async Task EnsureTaggedSubscriptionsAsync(IReadOnlyCollection<string> desired, CancellationToken cancellationToken)
    {
        if (_channel is null || _consumer is null) return;

        await _subscriptionGate.WaitAsync(cancellationToken);
        try
        {
            var desiredSet = new HashSet<string>(desired, StringComparer.Ordinal);

            foreach (var (existingQueue, consumerTag) in _taggedConsumers.ToArray())
            {
                if (desiredSet.Contains(existingQueue)) continue;
                try
                {
                    await _channel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
                    _taggedConsumers.TryRemove(existingQueue, out _);
                    _logger.LogInformation("Cancelled tagged subscription {Queue}", existingQueue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cancel tagged subscription {Queue}", existingQueue);
                }
            }

            foreach (var queue in desiredSet)
            {
                if (_taggedConsumers.ContainsKey(queue)) continue;
                try
                {
                    await _channel.QueueDeclareAsync(queue: queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
                    var tag = await _channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: _consumer, cancellationToken: cancellationToken);
                    _taggedConsumers[queue] = tag;
                    _logger.LogInformation("Subscribed to tagged queue {Queue}", queue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to subscribe to tagged queue {Queue}", queue);
                }
            }
        }
        finally
        {
            _subscriptionGate.Release();
        }
    }

    private async Task OnTaskReceived(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        TaskMessage? message = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            message = JsonSerializer.Deserialize<TaskMessage>(json);
            var nodeId = _configuration["NodeId"];

            if (message == null)
            {
                _logger.LogWarning("Received null task message, discarding");
                await _channel!.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                return;
            }

            _logger.LogInformation("Received task {InstanceId} type={TaskType} (NodeId={NodeId})",
                message.InstanceId, message.TaskType, message.NodeId);

            // P0-3a: shared-queue messages carry no NodeId. Send a claim so the
            // Cluster can bind the instance to this Node and transition the FSM
            // to Claimed before the handler runs.
            if (message.NodeId is null)
                await PublishClaimAsync(message.InstanceId, cancellationToken);

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
                NodeId = Guid.Parse(nodeId!)
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

        // P1-5a: pipeline pre-seeded with the three default sources. Handler
        // code calls ctx.Resolver.InterpolateAsync(rawJson) on the config text
        // it cares about, then parses the resolved JSON.
        var pipeline = new ValueResolverPipeline(new IValueResolver[]
        {
            new EnvStoreResolver(_envSecrets),
            new ParamResolver(runtimeParams),
            new ConfigResolver(staticConfig),
        });

        var handlerLogger = _loggerFactory.CreateLogger(handler.GetType());
        var context = new TaskContext(message, staticConfig, runtimeParams, pipeline, handlerLogger, cancellationToken);

        // P4-2a: expose the pipeline to the Serilog redaction enricher for the
        // duration of this handler invocation. Secrets resolved by the handler
        // are scrubbed from any log event emitted before the scope ends.
        using var redactionScope = SecretRedactionContext.Push(pipeline);
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

    private async Task PublishClaimAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        if (_channel == null) return;
        if (!Guid.TryParse(_configuration["NodeId"], out var nodeGuid))
        {
            _logger.LogError("Cannot publish claim — NodeId is not configured");
            return;
        }

        var claim = new TaskClaimMessage
        {
            InstanceId = instanceId,
            NodeId = nodeGuid,
            ClaimedAt = DateTime.UtcNow,
        };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claim));
        var props = new BasicProperties { Persistent = true, ContentType = "application/json" };

        await _channel.BasicPublishAsync(
            exchange: "", routingKey: ClaimQueueName, mandatory: false,
            basicProperties: props, body: body, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null) await _channel.DisposeAsync();
        if (_rabbitConnection != null) await _rabbitConnection.DisposeAsync();
    }
}

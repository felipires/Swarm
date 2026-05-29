using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Sdk.Wire;

namespace Swarm.Cluster.Services;

public class TaskResultConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<TaskResultConsumerService> _logger;
    private IModel? _channel;

    public const string ResultQueueName = "task-results";

    // P0-5: dead-letter topology. A transient Postgres outage during
    // UpdateInstanceAsync previously dropped the result permanently. Now:
    // first failure → nack(requeue:true) for one retry; second failure
    // (Redelivered=true) → nack(requeue:false) routes via the DLX into the
    // dead-letter queue for operator inspection / manual replay.
    public const string DeadLetterExchange = "task-results-dlx";
    public const string DeadLetterQueueName = "task-results-dead";

    public TaskResultConsumerService(IServiceProvider serviceProvider, IConnection rabbitConnection, ILogger<TaskResultConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _rabbitConnection = rabbitConnection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _rabbitConnection.CreateModel();

        // Dead-letter exchange + queue must exist before declaring the work
        // queue with the x-dead-letter-exchange argument that points at them.
        _channel.ExchangeDeclare(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);
        _channel.QueueDeclare(DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(DeadLetterQueueName, DeadLetterExchange, routingKey: ResultQueueName);

        // NOTE: this declaration carries x-dead-letter-exchange. If an
        // existing task-results queue was created without it (older deploys),
        // RabbitMQ will reject the redeclare with PRECONDITION_FAILED — the
        // queue must be deleted once during the upgrade. Documented in the
        // ROADMAP P0-5 entry.
        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = DeadLetterExchange,
        };
        _channel.QueueDeclare(ResultQueueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnResultReceived;

        _channel.BasicConsume(queue: ResultQueueName, autoAck: false, consumer: consumer);

        _logger.LogInformation(
            "Task result consumer started on queue '{Queue}' (DLX='{Dlx}', DLQ='{Dlq}')",
            ResultQueueName, DeadLetterExchange, DeadLetterQueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);

        _logger.LogInformation("Task result consumer stopping");
    }

    private async Task OnResultReceived(object sender, BasicDeliverEventArgs ea)
    {
        TaskResultMessage? result = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            result = JsonSerializer.Deserialize<TaskResultMessage>(json);

            if (result == null)
            {
                // Unparseable payload — drop straight to DLX, no retry.
                _logger.LogWarning("Received null task result message — routing to DLX");
                _channel!.BasicNack(ea.DeliveryTag, false, requeue: false);
                return;
            }

            await UpdateInstanceAsync(result);
            _channel!.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            // First delivery: requeue for one more attempt (covers transient DB blips).
            // Redelivered: give up and let the DLX route it to the dead-letter queue.
            var requeue = !ea.Redelivered;
            _logger.LogError(ex,
                "Error processing task result for instance {InstanceId} (Redelivered={Redelivered}, requeue={Requeue})",
                result?.InstanceId, ea.Redelivered, requeue);
            _channel!.BasicNack(ea.DeliveryTag, false, requeue: requeue);
        }
    }

    private async Task UpdateInstanceAsync(TaskResultMessage result)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();

        var instance = await db.TaskInstances.FindAsync(result.InstanceId);
        if (instance == null)
        {
            _logger.LogWarning("Received result for unknown task instance {InstanceId}", result.InstanceId);
            return;
        }

        var next = result.Success
            ? TaskInstance.TaskInstanceStatus.Completed
            : TaskInstance.TaskInstanceStatus.Failed;

        try
        {
            instance.Transition(next);
        }
        catch (InvalidOperationException ex)
        {
            // Result redelivery against an already-terminal instance — log and drop.
            // Without the FSM this would silently overwrite the Completed/Failed
            // record, which is the exact bug P0-2 exists to prevent.
            _logger.LogWarning(ex,
                "Ignoring out-of-order result for instance {InstanceId} (current Status={Status}, attempted={Next})",
                instance.Id, instance.Status, next);
            return;
        }

        instance.ResultJson = result.ResultJson;
        instance.ErrorMessage = result.ErrorMessage;
        instance.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Task instance {InstanceId} marked {Status}", result.InstanceId, instance.Status);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}


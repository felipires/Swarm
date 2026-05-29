using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Models;
using Swarm.Sdk.Wire;

namespace Swarm.Cluster.Services;

/// <summary>
/// Drains the <c>task-claims</c> queue (P0-3a). When a Node pulls a message
/// off a shared queue it publishes a <see cref="TaskClaimMessage"/>; we use
/// that to (a) bind the TaskInstance to the picking Node and (b) transition
/// Pending → Claimed.
/// </summary>
public class TaskClaimsConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<TaskClaimsConsumerService> _logger;
    private IModel? _channel;

    public const string ClaimQueueName = "task-claims";

    public TaskClaimsConsumerService(IServiceProvider serviceProvider, IConnection rabbitConnection, ILogger<TaskClaimsConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _rabbitConnection = rabbitConnection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _rabbitConnection.CreateModel();
        _channel.QueueDeclare(ClaimQueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnClaimReceived;

        _channel.BasicConsume(queue: ClaimQueueName, autoAck: false, consumer: consumer);
        _logger.LogInformation("Task claims consumer started on queue '{Queue}'", ClaimQueueName);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnClaimReceived(object sender, BasicDeliverEventArgs ea)
    {
        TaskClaimMessage? claim = null;
        try
        {
            var json = Encoding.UTF8.GetString(ea.Body.ToArray());
            claim = JsonSerializer.Deserialize<TaskClaimMessage>(json);

            if (claim is null)
            {
                _logger.LogWarning("Received null task claim message — dropping");
                _channel!.BasicNack(ea.DeliveryTag, false, requeue: false);
                return;
            }

            await ApplyClaimAsync(claim);
            _channel!.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            var requeue = !ea.Redelivered;
            _logger.LogError(ex,
                "Error processing task claim for instance {InstanceId} (Redelivered={Redelivered}, requeue={Requeue})",
                claim?.InstanceId, ea.Redelivered, requeue);
            _channel!.BasicNack(ea.DeliveryTag, false, requeue: requeue);
        }
    }

    private async Task ApplyClaimAsync(TaskClaimMessage claim)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.ClusterDbContext>();

        var instance = await db.TaskInstances.FindAsync(claim.InstanceId);
        if (instance is null)
        {
            _logger.LogWarning("Claim received for unknown instance {InstanceId}", claim.InstanceId);
            return;
        }

        try
        {
            instance.Transition(TaskInstance.TaskInstanceStatus.Claimed);
        }
        catch (InvalidOperationException ex)
        {
            // Out-of-order claim against an already-Dispatched/Completed/Failed
            // instance. The FSM rejects; log and drop without redelivery.
            _logger.LogWarning(ex,
                "Ignoring out-of-order claim for instance {InstanceId} (current Status={Status})",
                instance.Id, instance.Status);
            return;
        }

        instance.NodeId = claim.NodeId;
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Instance {InstanceId} claimed by node {NodeId}", instance.Id, claim.NodeId);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}

using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

public class TaskResultConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<TaskResultConsumerService> _logger;
    private IModel? _channel;

    public const string ResultQueueName = "task-results";

    public TaskResultConsumerService(IServiceProvider serviceProvider, IConnection rabbitConnection, ILogger<TaskResultConsumerService> logger)
    {
        _serviceProvider = serviceProvider;
        _rabbitConnection = rabbitConnection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _rabbitConnection.CreateModel();
        _channel.QueueDeclare(queue: ResultQueueName, durable: true, exclusive: false, autoDelete: false);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnResultReceived;

        _channel.BasicConsume(queue: ResultQueueName, autoAck: false, consumer: consumer);

        _logger.LogInformation("Task result consumer started on queue '{Queue}'", ResultQueueName);

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
                _logger.LogWarning("Received null task result message");
                _channel!.BasicNack(ea.DeliveryTag, false, false);
                return;
            }

            await UpdateInstanceAsync(result);
            _channel!.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing task result for instance {InstanceId}", result?.InstanceId);
            _channel!.BasicNack(ea.DeliveryTag, false, requeue: false);
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

        instance.Status = result.Success
            ? TaskInstance.TaskInstanceStatus.Completed
            : TaskInstance.TaskInstanceStatus.Failed;
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

public record TaskResultMessage
{
    public Guid InstanceId { get; init; }
    public bool Success { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorMessage { get; init; }
}

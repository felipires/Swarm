using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Drains the <c>PendingDispatch</c> outbox by publishing to RabbitMQ and
/// stamping <c>PublishedAt</c> + transitioning the linked TaskInstance to
/// <c>Dispatched</c>. Roadmap P0-4.
/// </summary>
public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<OutboxPublisherService> _logger;

    private const int BatchSize = 50;
    private const int IdlePollIntervalMs = 1000;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        IConnection rabbitConnection,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _rabbitConnection = rabbitConnection;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox publisher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(IdlePollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox publisher loop error; sleeping before retry");
                await Task.Delay(IdlePollIntervalMs, stoppingToken);
            }
        }

        _logger.LogInformation("Outbox publisher stopping");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClusterDbContext>();

        var pending = await db.PendingDispatches
            .Where(p => p.PublishedAt == null)
            .OrderBy(p => p.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        using var channel = _rabbitConnection.CreateModel();
        var declaredQueues = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in pending)
        {
            try
            {
                if (declaredQueues.Add(row.QueueName))
                {
                    channel.QueueDeclare(
                        queue: row.QueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false);
                }

                var body = Encoding.UTF8.GetBytes(row.Payload);
                var props = channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: "",
                    routingKey: row.QueueName,
                    basicProperties: props,
                    body: body);

                row.PublishedAt = DateTime.UtcNow;
                row.LastError = null;

                var instance = await db.TaskInstances.FindAsync([row.InstanceId], ct);
                if (instance is not null && instance.Status == TaskInstance.TaskInstanceStatus.Pending)
                {
                    instance.Transition(TaskInstance.TaskInstanceStatus.Dispatched);
                    instance.DispatchedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                row.Attempts += 1;
                row.LastError = ex.Message;
                _logger.LogError(ex,
                    "Outbox publish failed for dispatch {DispatchId} (instance {InstanceId}, attempts={Attempts})",
                    row.Id, row.InstanceId, row.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        return pending.Count;
    }
}

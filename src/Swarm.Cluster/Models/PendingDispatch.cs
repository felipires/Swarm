namespace Swarm.Cluster.Models;

/// <summary>
/// Outbox row (roadmap P0-4). Written in the same DB transaction as a new
/// <see cref="TaskInstance"/> so the dispatch decision is atomic with respect
/// to the broker publish. The <c>OutboxPublisherService</c> picks up rows
/// where <see cref="PublishedAt"/> is null, publishes them to RabbitMQ, then
/// stamps <see cref="PublishedAt"/> and transitions the linked TaskInstance
/// to <c>Dispatched</c>.
/// </summary>
public class PendingDispatch
{
    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }

    /// <summary>RabbitMQ routing target (default exchange routes by queue name).</summary>
    public string QueueName { get; set; } = null!;

    /// <summary>Serialized <c>TaskMessage</c> as JSON. Stored as jsonb.</summary>
    public string Payload { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

namespace Swarm.Sdk.Wire;

/// <summary>
/// Published by a Node when it pulls a message off a shared queue, before it
/// invokes the handler. The Cluster's <c>TaskClaimsConsumerService</c> uses
/// this to bind <see cref="TaskMessage.NodeId"/> on the persisted
/// <c>TaskInstance</c> and transition Pending → Claimed (roadmap D1 / P0-3a).
/// </summary>
public record TaskClaimMessage
{
    public Guid InstanceId { get; init; }
    public Guid NodeId { get; init; }
    public DateTime ClaimedAt { get; init; }
}

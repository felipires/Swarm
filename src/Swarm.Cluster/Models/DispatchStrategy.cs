namespace Swarm.Cluster.Models;

/// <summary>
/// How the Cluster routes a dispatched TaskInstance to a Node (roadmap D1).
/// </summary>
public enum DispatchStrategy
{
    /// <summary>Send to one named Node. Per-node queue. NodeId set at dispatch.</summary>
    SpecificNode = 0,

    /// <summary>Broadcast: one TaskInstance per online Node, each on its own per-node queue.</summary>
    AllOnlineNodes = 1,

    /// <summary>
    /// Any online Node that advertises the TaskType picks it up. Uses a shared
    /// queue (<c>tasks.shared.&lt;taskType&gt;</c>) with competing consumers and
    /// fair prefetch. <c>TaskInstance.NodeId</c> starts NULL and is set by
    /// <c>TaskClaimsConsumerService</c> after the Node publishes a claim.
    /// </summary>
    AnyOnlineNode = 2,

    /// <summary>
    /// Like <see cref="AnyOnlineNode"/> but constrained to Nodes whose effective
    /// tags satisfy the target tag selector. Phase 1: Cluster resolves an
    /// eligible Node at dispatch time and routes as <see cref="SpecificNode"/>.
    /// Full shared <c>tasks.tagged.&lt;hash&gt;</c> queue with dynamic Node
    /// subscription is a follow-up.
    /// </summary>
    TaggedNodes = 3,
}

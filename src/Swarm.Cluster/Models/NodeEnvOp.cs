namespace Swarm.Cluster.Models;

/// <summary>
/// Pending task-config env mutation that the Cluster relays to a Node on its
/// next heartbeat (P1-5a). The Cluster stores plaintext values briefly — they
/// are deleted once the Node acks the operation in a subsequent heartbeat.
///
/// This is a pragmatic departure from the roadmap's "Cluster never holds
/// plaintext" rule: the alternative is a gRPC server on the Node (requires
/// Web SDK conversion + inbound connectivity), which is out of scope for
/// the deferred-items batch. The window is bounded by heartbeat interval
/// (default 30s); operators who care can run a Cluster-side encryption-at-
/// rest layer (e.g. Postgres TDE) underneath.
/// </summary>
public class NodeEnvOp
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>0 = Set, 1 = Delete.</summary>
    public EnvOpKind Op { get; set; }

    public string Key { get; set; } = null!;

    /// <summary>Non-null for <see cref="EnvOpKind.Set"/>; ignored for Delete.</summary>
    public string? Value { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Set when the op is included in a heartbeat response. The next
    /// heartbeat from the Node either acks it (we delete the row) or, if
    /// stale beyond a grace window, we re-include it.
    /// </summary>
    public DateTime? LastSentAt { get; set; }

    public enum EnvOpKind
    {
        Set = 0,
        Delete = 1,
    }
}

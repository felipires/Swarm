namespace Swarm.Cluster.Models;

/// <summary>
/// Task-config env key record and pending mutation relay (P1-5a).
///
/// Lifecycle for Set ops: the Value is encrypted at rest on the Cluster
/// (<see cref="IsSecret"/> = true) or stored plaintext (false). On delivery
/// the Node applies the value; on ack the Cluster nulls <see cref="Value"/>
/// and keeps the row permanently as a key-name inventory record.
/// Lifecycle for Delete ops: row is removed entirely once acked.
///
/// <see cref="IsSecret"/> = true → Cluster encrypts Value (AES-256-GCM,
/// <c>Env:EncryptionKey</c>), Node stores in <c>EnvSecretsStore</c> (Tier 2).
/// <see cref="IsSecret"/> = false → plaintext both sides, Node stores in
/// <c>PlaintextConfigStore</c> (Tier 3 — feature flags, non-secret URLs).
/// </summary>
public class NodeEnvOp
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>0 = Set, 1 = Delete.</summary>
    public EnvOpKind Op { get; set; }

    public string Key { get; set; } = null!;

    /// <summary>Non-null for <see cref="EnvOpKind.Set"/>; ignored for Delete.
    /// Nulled out after the Node acks the op — the row is kept as a key-name
    /// inventory record.</summary>
    public string? Value { get; set; }

    /// <summary>When true the value is encrypted at rest on the Cluster and
    /// the Node stores it in its encrypted <c>EnvSecretsStore</c> (Tier 2).
    /// When false the value is stored plaintext on both sides (Tier 3 — for
    /// non-secret operational config like feature flags or base URLs).</summary>
    public bool IsSecret { get; set; } = true;

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

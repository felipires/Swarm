using System;

namespace Swarm.Cluster.Models;

public class Log
{
    public Guid Id { get; set; }

    /// <summary>
    /// Origin node. Nullable: cluster-origin logs (e.g. pipeline-run lifecycle
    /// events) are not tied to a node.
    /// </summary>
    public Guid? NodeId { get; set; }
    public string Level { get; set; } = null!;
    public string MessageTemplate { get; set; } = null!;
    public string? Message { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; } // JSON serialized properties

    /// <summary>
    /// Extensible correlation/context tags as a jsonb object (string→string),
    /// e.g. { "task": "...", "run": "...", "pipeline": "...", "env.DB": "prod" }.
    /// Stored as a JSON string and GIN-indexed (jsonb_path_ops) so log search
    /// filters via containment (<c>Tags @&gt; @json</c>) — same pattern as
    /// Node.EffectiveTagsJson. Arbitrary keys are allowed (no schema change).
    /// </summary>
    public string? Tags { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; }
}

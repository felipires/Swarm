namespace Swarm.Cluster.Models;

public class TaskDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>
    /// Task type identifier including version (e.g. <c>"http@1"</c>). Always
    /// carries an explicit version per roadmap decision D3 — schema changes
    /// produce a new version, never a mutation of an existing one. Defaults
    /// to <c>"default@1"</c> so pre-versioning rows continue to dispatch
    /// against the built-in passthrough handler.
    /// </summary>
    public string TaskType { get; set; } = "default@1";

    /// <summary>
    /// Default dispatch strategy when a dispatch request doesn't pick one.
    /// Defaults to <see cref="DispatchStrategy.SpecificNode"/> to preserve the
    /// existing behavior (named NodeId at dispatch).
    /// </summary>
    public DispatchStrategy DefaultStrategy { get; set; } = DispatchStrategy.SpecificNode;

    /// <summary>
    /// Default tag selector (JSON-encoded <c>Dictionary&lt;string, string&gt;</c>)
    /// for <see cref="DispatchStrategy.TaggedNodes"/>. Ignored for other strategies.
    /// </summary>
    public string? DefaultTargetTagsJson { get; set; }

    /// <summary>JSON payload passed to the node when executing this task.</summary>
    public string ConfigJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<TaskInstance> Instances { get; set; } = new List<TaskInstance>();
}

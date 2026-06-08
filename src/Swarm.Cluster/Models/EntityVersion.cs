namespace Swarm.Cluster.Models;

/// <summary>
/// One immutable history entry for a versioned definition (P1-10). Written on
/// every create / update / restore of a <see cref="TaskDefinition"/> or
/// <see cref="Pipeline"/>. Append-only: numbers are never reused and rows are
/// never mutated. <see cref="SnapshotJson"/> holds the create-request-shaped
/// definition at that version, so a restore can re-apply it through the normal
/// create/update path and the UI can render it with existing components.
/// </summary>
public class EntityVersion
{
    public Guid Id { get; set; }

    public VersionedEntityType EntityType { get; set; }

    /// <summary>TaskDefinitionId or PipelineId (polymorphic — no FK).</summary>
    public Guid EntityId { get; set; }

    public int Version { get; set; }

    /// <summary>JSON snapshot of the definition at this version (jsonb).</summary>
    public string SnapshotJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
}

public enum VersionedEntityType
{
    Task,
    Pipeline,
}

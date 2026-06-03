namespace Swarm.Cluster.Models;

/// <summary>
/// A directed acyclic graph of <see cref="PipelineStep"/>s. Operator-defined
/// once, run many times via <see cref="PipelineRun"/>. Versioning mirrors
/// <see cref="TaskDefinition.Version"/> — edits increment <see cref="Version"/>
/// and the next run captures the new version, but in-flight runs are
/// insulated by <see cref="PipelineRun.StepsSnapshotJson"/>.
/// </summary>
public class Pipeline
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>Monotonically increasing on every structural edit (P1-1a).</summary>
    public int Version { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<PipelineStep> Steps { get; set; } = new List<PipelineStep>();
}

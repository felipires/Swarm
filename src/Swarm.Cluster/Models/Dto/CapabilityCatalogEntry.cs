namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// One distinct TaskType@version the cluster can currently run, with the
/// handler's declared schema and resolution requirements (P0-3b). Aggregated
/// across all Nodes that advertise it. Drives the task-authoring UI.
/// </summary>
public class CapabilityCatalogEntry
{
    public string TaskType { get; set; } = null!;

    /// <summary>JSON Schema (draft 2020-12) describing the handler's ConfigJson.</summary>
    public string JsonSchema { get; set; } = "{}";

    /// <summary>Env keys the handler requires in the Node env store.</summary>
    public List<string> RequiredEnvKeys { get; set; } = new();

    /// <summary>Runtime param keys the handler requires at dispatch time.</summary>
    public List<string> RequiredParams { get; set; } = new();

    /// <summary>How many online-or-known Nodes advertise this handler.</summary>
    public int NodeCount { get; set; }
}

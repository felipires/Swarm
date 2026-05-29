namespace Swarm.Cluster.Models;

/// <summary>
/// One row per (Node, TaskType) declaring that the Node can execute a given
/// handler, plus its declared schema and resolution requirements (roadmap
/// P0-3b). Rebuilt wholesale on every <c>RegisterNode</c> RPC.
/// </summary>
public class NodeCapability
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; }

    /// <summary>TaskType@version identifier (e.g. <c>"http@1"</c>).</summary>
    public string TaskType { get; set; } = null!;

    /// <summary>JSON Schema (draft 2020-12) describing the handler's ConfigJson shape.</summary>
    public string JsonSchema { get; set; } = "{}";

    /// <summary>JSON-encoded list of env keys the handler requires.</summary>
    public string RequiredEnvKeysJson { get; set; } = "[]";

    /// <summary>JSON-encoded list of runtime param keys the handler requires.</summary>
    public string RequiredParamsJson { get; set; } = "[]";

    public DateTime ReportedAt { get; set; }
}

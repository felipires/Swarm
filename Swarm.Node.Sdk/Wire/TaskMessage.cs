namespace Swarm.Node.Sdk.Wire;

/// <summary>
/// Canonical wire payload published by the Cluster to a Node's task queue
/// and consumed by <c>TaskExecutorService</c>. Field names are part of the
/// wire contract — they appear verbatim in JSON.
/// </summary>
public record TaskMessage
{
    public Guid InstanceId { get; init; }
    public Guid TaskDefinitionId { get; init; }

    /// <summary>
    /// Target Node identifier. Nullable to allow shared-queue strategies
    /// (per roadmap D1) where a message is claimed by whichever Node picks it up.
    /// </summary>
    public Guid? NodeId { get; init; }

    /// <summary>
    /// Task type identifier including version (e.g. <c>"http@1"</c>).
    /// Defaults to <c>"default@1"</c> so messages produced by an older Cluster
    /// remain backward-compatible.
    /// </summary>
    public string TaskType { get; init; } = "default@1";

    public string ConfigJson { get; init; } = "{}";

    public string? RuntimeParamsJson { get; init; }
}

/// <summary>
/// Result envelope published back to the Cluster's <c>task-results</c> queue
/// after a handler completes (successfully or not).
/// </summary>
public record TaskResultMessage
{
    public Guid InstanceId { get; init; }
    public bool Success { get; init; }
    public string? ResultJson { get; init; }
    public string? ErrorMessage { get; init; }
}

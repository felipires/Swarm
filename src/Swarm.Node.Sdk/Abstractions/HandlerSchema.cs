namespace Swarm.Node.Sdk.Abstractions;

/// <summary>
/// Declared shape of a handler's configuration and runtime requirements.
/// Reported to the Cluster at Node registration so dispatch-time validation
/// can fail fast before a message ever hits the queue.
/// </summary>
public record HandlerSchema
{
    /// <summary>JSON Schema (draft 2020-12) describing the handler's ConfigJson shape.</summary>
    public string JsonSchema { get; init; } = "{}";

    /// <summary>Env keys the handler requires in the Node's env store (resolved by P1-5a).</summary>
    public IReadOnlyList<string> RequiredEnvKeys { get; init; } = [];

    /// <summary>Runtime param keys the handler requires at dispatch time.</summary>
    public IReadOnlyList<string> RequiredParams { get; init; } = [];
}

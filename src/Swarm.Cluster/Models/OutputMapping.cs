namespace Swarm.Cluster.Models;

/// <summary>
/// One entry in a step's output-mapping list (P1-8). After the named
/// upstream step completes, the Cluster walks <see cref="FromPath"/> into
/// its <c>ResultJson</c> and injects the extracted value into this step's
/// runtime params under <see cref="ToParam"/>, making it accessible as
/// <c>{param:ToParam}</c> in config placeholders.
/// </summary>
public record OutputMapping(
    /// <summary>Name of the upstream step whose ResultJson is the source.</summary>
    string FromStep,

    /// <summary>
    /// Dot/bracket path into the upstream ResultJson, e.g. <c>rows[0].email</c>.
    /// Empty string returns the entire ResultJson as a raw string.
    /// </summary>
    string FromPath,

    /// <summary>Runtime param key injected into this step's dispatch params.</summary>
    string ToParam);

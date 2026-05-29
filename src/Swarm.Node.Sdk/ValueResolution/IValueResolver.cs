namespace Swarm.Sdk.ValueResolution;

/// <summary>
/// Resolves a single placeholder source (e.g. <c>env</c>, <c>param</c>,
/// <c>config</c>). Implementations are pure source readers — modifier handling
/// (required, secret, default, type) is the pipeline's job.
/// </summary>
public interface IValueResolver
{
    /// <summary>The source identifier this resolver answers for, e.g. <c>"env"</c>.</summary>
    string Source { get; }

    Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// A single resolved value. <see cref="IsSecret"/> propagates up the pipeline
/// so the redaction enricher (P4-2a) can scrub the raw string from logs.
/// </summary>
public record ResolvedValue(string Raw, bool IsSecret = false);

using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.ValueResolution;

/// <summary>
/// Resolves <c>env:KEY</c> placeholders from the Node's task-env store
/// (roadmap P1-5a). Phase-1 scope: Tier 1 only — process env vars prefixed
/// with <c>SWARM_TASKENV_</c>. Tier 2 (local encrypted SQLite secrets) and
/// Tier 3 (Cluster-pushed plaintext config) are tracked follow-ups.
/// </summary>
public class EnvStoreResolver : IValueResolver
{
    public const string EnvVarPrefix = "SWARM_TASKENV_";

    public string Source => "env";

    public Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
    {
        var value = Environment.GetEnvironmentVariable(EnvVarPrefix + key);
        return Task.FromResult<ResolvedValue?>(
            value is null ? null : new ResolvedValue(value, IsSecret: false));
    }
}

using Swarm.Node.Data;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.ValueResolution;

/// <summary>
/// Resolves <c>env:KEY</c> placeholders from the Node's task-env store
/// (roadmap P1-5a). Tier order (first hit wins):
///   1. Process env vars prefixed with <c>SWARM_TASKENV_</c>
///   2. Local encrypted SQLite store (<see cref="EnvSecretsStore"/>)
///
/// Tier 3 (Cluster-pushed plaintext config) is a follow-up: the operational
/// path for non-secret task config that the Cluster can see in clear.
/// Values resolved from Tier 2 are flagged <c>IsSecret = true</c> so the
/// redaction enricher (P4-2a) scrubs them from logs even without an explicit
/// <c>:secret</c> modifier on the placeholder.
/// </summary>
public class EnvStoreResolver : IValueResolver
{
    public const string EnvVarPrefix = "SWARM_TASKENV_";

    private readonly EnvSecretsStore? _store;

    /// <summary>Parameterless ctor for tests that only need Tier 1.</summary>
    public EnvStoreResolver() : this(null) { }

    public EnvStoreResolver(EnvSecretsStore? store)
    {
        _store = store;
    }

    public string Source => "env";

    public async Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
    {
        // Tier 1: process env override.
        var value = Environment.GetEnvironmentVariable(EnvVarPrefix + key);
        if (value is not null)
            return new ResolvedValue(value, IsSecret: false);

        // Tier 2: encrypted SQLite store.
        if (_store is not null)
        {
            var encrypted = await _store.GetAsync(key, cancellationToken);
            if (encrypted is not null)
                return new ResolvedValue(encrypted, IsSecret: true);
        }

        return null;
    }
}

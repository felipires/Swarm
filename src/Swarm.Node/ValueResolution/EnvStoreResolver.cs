using Swarm.Node.Data;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.ValueResolution;

/// <summary>
/// Resolves <c>env:KEY</c> placeholders from the Node's task-env stores.
/// Tier order (first hit wins):
///   1. Process env vars prefixed with <c>SWARM_TASKENV_</c>
///   2. Local encrypted SQLite store (<see cref="EnvSecretsStore"/>) — Tier 2
///   3. Cluster-pushed plaintext SQLite store (<see cref="PlaintextConfigStore"/>) — Tier 3
///
/// Values from Tier 2 are flagged <c>IsSecret = true</c> so the redaction
/// enricher (P4-2a) scrubs them from logs even without an explicit <c>:secret</c>
/// modifier on the placeholder. Tier 3 values are non-secret (IsSecret = false).
/// </summary>
public class EnvStoreResolver : IValueResolver
{
    public const string EnvVarPrefix = "SWARM_TASKENV_";

    private readonly EnvSecretsStore? _secrets;
    private readonly PlaintextConfigStore? _config;

    /// <summary>Parameterless ctor for tests that only need Tier 1.</summary>
    public EnvStoreResolver() : this(null, null) { }

    public EnvStoreResolver(EnvSecretsStore? secrets, PlaintextConfigStore? config = null)
    {
        _secrets = secrets;
        _config = config;
    }

    public string Source => "env";

    public async Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
    {
        // Tier 1: process env override.
        var value = Environment.GetEnvironmentVariable(EnvVarPrefix + key);
        if (value is not null)
            return new ResolvedValue(value, IsSecret: false);

        // Tier 2: encrypted SQLite store.
        if (_secrets is not null)
        {
            var encrypted = await _secrets.GetAsync(key, cancellationToken);
            if (encrypted is not null)
                return new ResolvedValue(encrypted, IsSecret: true);
        }

        // Tier 3: plaintext config pushed by Cluster.
        if (_config is not null)
        {
            var plain = await _config.GetAsync(key, cancellationToken);
            if (plain is not null)
                return new ResolvedValue(plain, IsSecret: false);
        }

        return null;
    }
}

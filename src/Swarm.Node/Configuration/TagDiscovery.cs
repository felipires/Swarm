namespace Swarm.Node.Configuration;

/// <summary>
/// Reads Node-local static tags from two sources per roadmap D6:
///   1. <c>Swarm:Tags</c> section in appsettings.json
///   2. <c>SWARM_TAG_&lt;key&gt;=&lt;value&gt;</c> process env vars
///
/// Env-var keys are lowercased on read so <c>SWARM_TAG_REGION=eu</c> becomes
/// <c>region=eu</c>. On conflict between the two sources, the env var wins
/// (more specific deploy-time override beats baked-in config).
/// </summary>
public static class TagDiscovery
{
    public const string EnvVarPrefix = "SWARM_TAG_";

    public static IReadOnlyDictionary<string, string> Discover(IConfiguration configuration)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var section = configuration.GetSection("Swarm:Tags");
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.Value))
                tags[child.Key] = child.Value;
        }

        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            var pair = (System.Collections.DictionaryEntry)entry!;
            var key = pair.Key as string;
            if (key is null || !key.StartsWith(EnvVarPrefix, StringComparison.Ordinal)) continue;
            if (pair.Value is not string value || value.Length == 0) continue;

            tags[key[EnvVarPrefix.Length..].ToLowerInvariant()] = value;
        }

        return tags;
    }
}

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Swarm.Node.Configuration;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// Static tag discovery (P2-5): both appsettings (<c>Swarm:Tags</c>) and
/// process env vars (<c>SWARM_TAG_*</c>) feed the same dictionary; env vars
/// win on key conflict because they represent deploy-time overrides.
/// </summary>
public class TagDiscoveryTests : IDisposable
{
    private readonly List<string> _envKeysToClear = new();

    [Fact]
    public void Discover_FromAppsettingsOnly_ReturnsAllChildren()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Swarm:Tags:env"] = "prod",
                ["Swarm:Tags:region"] = "eu",
            })
            .Build();

        var tags = TagDiscovery.Discover(config);

        tags.Should().HaveCount(2);
        tags["env"].Should().Be("prod");
        tags["region"].Should().Be("eu");
    }

    [Fact]
    public void Discover_FromEnvVarOnly_LowercasesKeyAndStripsPrefix()
    {
        SetEnv("SWARM_TAG_REGION", "us-east");
        SetEnv("SWARM_TAG_TEAM", "data");
        SetEnv("UNRELATED", "ignored");

        var config = new ConfigurationBuilder().Build();
        var tags = TagDiscovery.Discover(config);

        tags.Should().HaveCount(2);
        tags["region"].Should().Be("us-east");
        tags["team"].Should().Be("data");
        tags.ContainsKey("unrelated").Should().BeFalse();
    }

    [Fact]
    public void Discover_EnvVarOverridesAppsettingsOnConflict()
    {
        SetEnv("SWARM_TAG_REGION", "us-east");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Swarm:Tags:region"] = "eu",
            })
            .Build();

        var tags = TagDiscovery.Discover(config);

        tags["region"].Should().Be("us-east", "env vars are the deploy-time override layer");
    }

    [Fact]
    public void Discover_EmptyEnvValue_IsSkipped()
    {
        SetEnv("SWARM_TAG_EMPTY", "");

        var config = new ConfigurationBuilder().Build();
        var tags = TagDiscovery.Discover(config);

        tags.ContainsKey("empty").Should().BeFalse();
    }

    private void SetEnv(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _envKeysToClear.Add(key);
    }

    public void Dispose()
    {
        foreach (var key in _envKeysToClear)
            Environment.SetEnvironmentVariable(key, null);
        GC.SuppressFinalize(this);
    }
}

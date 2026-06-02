using FluentAssertions;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// The tagged-route hash must be deterministic and order-independent so both
/// sides (Cluster picking the queue, Node deciding what to subscribe to)
/// agree without an out-of-band naming convention.
/// </summary>
public class TaggedRouteHashTests
{
    [Fact]
    public void Compute_KeyOrderIrrelevant_SameHash()
    {
        var a = new Dictionary<string, string> { ["region"] = "eu", ["env"] = "prod" };
        var b = new Dictionary<string, string> { ["env"] = "prod", ["region"] = "eu" };

        TaggedRouteHash.Compute(a).Hash.Should().Be(TaggedRouteHash.Compute(b).Hash);
    }

    [Fact]
    public void Compute_DifferentValues_DifferentHash()
    {
        var a = new Dictionary<string, string> { ["env"] = "prod" };
        var b = new Dictionary<string, string> { ["env"] = "staging" };

        TaggedRouteHash.Compute(a).Hash.Should().NotBe(TaggedRouteHash.Compute(b).Hash);
    }

    [Fact]
    public void QueueNameFor_UsesHashPrefix()
    {
        var selector = new Dictionary<string, string> { ["env"] = "prod" };

        var name = TaggedRouteHash.QueueNameFor(selector);

        name.Should().StartWith("tasks.tagged.");
        name.Length.Should().Be("tasks.tagged.".Length + 16);
    }

    [Fact]
    public void Compute_CanonicalJson_HasKeysInOrder()
    {
        var selector = new Dictionary<string, string>
        {
            ["zeta"] = "9",
            ["alpha"] = "1",
            ["middle"] = "5",
        };

        var (_, canonical) = TaggedRouteHash.Compute(selector);

        var alphaIdx = canonical.IndexOf("\"alpha\"", StringComparison.Ordinal);
        var middleIdx = canonical.IndexOf("\"middle\"", StringComparison.Ordinal);
        var zetaIdx = canonical.IndexOf("\"zeta\"", StringComparison.Ordinal);

        alphaIdx.Should().BeLessThan(middleIdx);
        middleIdx.Should().BeLessThan(zetaIdx);
    }
}

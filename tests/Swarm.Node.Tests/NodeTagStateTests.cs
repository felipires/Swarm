using FluentAssertions;
using Swarm.Node.Configuration;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// Tag-layer merge semantics (D6): static is the Node's deploy-time identity
/// and must win when both layers carry the same key. Overlay is operational
/// and only contributes keys the static layer doesn't already own.
/// </summary>
public class NodeTagStateTests
{
    [Fact]
    public void Effective_NoOverlap_UnionsBothLayers()
    {
        var state = new NodeTagState();
        state.SetStatic(new Dictionary<string, string> { ["env"] = "prod" });
        state.SetOverlay(new Dictionary<string, string> { ["priority"] = "high" });

        state.Effective.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["env"] = "prod",
            ["priority"] = "high",
        });
    }

    [Fact]
    public void Effective_KeyConflict_StaticWins()
    {
        var state = new NodeTagState();
        state.SetStatic(new Dictionary<string, string> { ["region"] = "eu" });
        state.SetOverlay(new Dictionary<string, string> { ["region"] = "us-east" });

        state.Effective["region"].Should().Be("eu",
            "static tags are the Node's deploy-time identity and cannot be overridden by operational overlays");
    }

    [Fact]
    public void Overlay_RefreshReplacesPreviousSet()
    {
        var state = new NodeTagState();
        state.SetOverlay(new Dictionary<string, string> { ["maintenance"] = "true" });
        state.SetOverlay(new Dictionary<string, string> { ["priority"] = "low" });

        state.Overlay.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["priority"] = "low",
        });
        state.Overlay.ContainsKey("maintenance").Should().BeFalse(
            "each heartbeat tick replaces the whole overlay set, it doesn't merge");
    }

    [Fact]
    public void Static_KeyLookupIsCaseInsensitive()
    {
        var state = new NodeTagState();
        state.SetStatic(new Dictionary<string, string> { ["Env"] = "prod" });

        state.Static.ContainsKey("env").Should().BeTrue();
        state.Static["ENV"].Should().Be("prod");
    }
}

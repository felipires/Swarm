using System.Text.Json;
using FluentAssertions;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services.Tags;
using Xunit;

namespace Swarm.Cluster.Tests;

public class EffectiveTagsTests
{
    private static NodeOverlayTag Ov(string k, string v)
        => new() { Id = Guid.NewGuid(), NodeId = Guid.Empty, Key = k, Value = v };

    [Fact]
    public void Compose_StaticOnly_ReturnsStatic()
    {
        var staticJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "eu" });
        var merged = EffectiveTags.Compose(staticJson, Array.Empty<NodeOverlayTag>());
        merged.Should().BeEquivalentTo(new Dictionary<string, string> { ["region"] = "eu" });
    }

    [Fact]
    public void Compose_OverlayOnly_ReturnsOverlay()
    {
        var merged = EffectiveTags.Compose(null, new[] { Ov("priority", "high") });
        merged.Should().BeEquivalentTo(new Dictionary<string, string> { ["priority"] = "high" });
    }

    [Fact]
    public void Compose_Conflict_StaticWins()
    {
        var staticJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["region"] = "eu" });
        var merged = EffectiveTags.Compose(staticJson, new[] { Ov("region", "us"), Ov("priority", "high") });
        merged["region"].Should().Be("eu", "static wins on key conflict (D6)");
        merged["priority"].Should().Be("high");
    }

    [Fact]
    public void Compose_CaseInsensitiveKeys()
    {
        var staticJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Region"] = "eu" });
        var merged = EffectiveTags.Compose(staticJson, new[] { Ov("region", "us") });
        merged.Should().HaveCount(1);
        merged["REGION"].Should().Be("eu");
    }

    [Fact]
    public void Compose_MalformedStatic_TreatedAsEmpty()
    {
        var merged = EffectiveTags.Compose("not json", new[] { Ov("a", "b") });
        merged.Should().BeEquivalentTo(new Dictionary<string, string> { ["a"] = "b" });
    }

    [Fact]
    public void Serialize_OrderIndependent_Canonical()
    {
        var a = EffectiveTags.Serialize(new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" });
        var b = EffectiveTags.Serialize(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        a.Should().Be(b);
        a.Should().Be("""{"a":"1","b":"2"}""");
    }

    [Fact]
    public void Serialize_Empty_ReturnsNull()
    {
        EffectiveTags.Serialize(new Dictionary<string, string>()).Should().BeNull();
    }

    [Fact]
    public void ComposeJson_EmptyEffective_ReturnsNull()
    {
        EffectiveTags.ComposeJson(null, Array.Empty<NodeOverlayTag>()).Should().BeNull();
    }
}

using FluentAssertions;
using Swarm.Sdk.ValueResolution;
using Xunit;

namespace Swarm.Sdk.Tests;

public class PlaceholderParserTests
{
    [Fact]
    public void Extract_SinglePlaceholder_ReturnsSourceAndKey()
    {
        var placeholders = PlaceholderParser.Extract("hello {env:API_TOKEN} world");

        placeholders.Should().HaveCount(1);
        placeholders[0].Source.Should().Be("env");
        placeholders[0].Key.Should().Be("API_TOKEN");
        placeholders[0].Modifiers.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ModifiersAreSplitAndTrimmed()
    {
        var placeholders = PlaceholderParser.Extract("{env:TOKEN:required:secret}");

        placeholders[0].Modifiers.Should().Equal(new[] { "required", "secret" });
    }

    [Fact]
    public void Extract_MultiplePlaceholders_PreservesSourceOrder()
    {
        var placeholders = PlaceholderParser.Extract("{config:a} and {param:b} and {env:c}");

        placeholders.Should().HaveCount(3);
        placeholders.Select(p => p.Source).Should().Equal(new[] { "config", "param", "env" });
    }

    [Fact]
    public void Extract_JsonObjectBraces_AreNotMatched()
    {
        // The strict regex must not greedily consume JSON braces as a
        // placeholder body. This is the realistic template shape.
        var placeholders = PlaceholderParser.Extract(
            """{"url":"https://example.com/{param:id}","headers":{"X":"Y"}}""");

        placeholders.Should().HaveCount(1);
        placeholders[0].Source.Should().Be("param");
        placeholders[0].Key.Should().Be("id");
    }

    [Fact]
    public void Extract_MalformedPlaceholder_Skipped()
    {
        // No colon at all → not a placeholder by our syntax rules.
        PlaceholderParser.Extract("{noColon}").Should().BeEmpty();
        // Uppercase source not allowed.
        PlaceholderParser.Extract("{ENV:KEY}").Should().BeEmpty();
        // Empty key.
        PlaceholderParser.Extract("{env:}").Should().BeEmpty();
    }

    [Fact]
    public void Extract_PositionAndLength_AllowSliceReplacement()
    {
        var template = "abc{env:X}def";
        var p = PlaceholderParser.Extract(template).Single();

        template.Substring(p.Start, p.Length).Should().Be("{env:X}");
    }
}

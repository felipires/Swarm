using FluentAssertions;
using Swarm.Node.Sdk;
using Xunit;

namespace Swarm.Node.Sdk.Tests;

public class TaskTypeIdTests
{
    [Theory]
    [InlineData("http@1", "http", 1)]
    [InlineData("sql@2", "sql", 2)]
    [InlineData("default@1", "default", 1)]
    [InlineData("a@10", "a", 10)]
    [InlineData("snake_case-name@99", "snake_case-name", 99)]
    public void Parse_ValidIdentifier_ReturnsNameAndVersion(string input, string expectedName, int expectedVersion)
    {
        var id = TaskTypeId.Parse(input);

        id.Name.Should().Be(expectedName);
        id.Version.Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData("Http@1")]      // uppercase first char
    [InlineData("HTTP@1")]      // all caps
    [InlineData("http@0")]      // version zero
    [InlineData("http@01")]     // leading zero
    [InlineData("http")]        // missing @version
    [InlineData("http@")]       // empty version
    [InlineData("@1")]          // empty name
    [InlineData("http@1.0")]    // non-integer version
    [InlineData("http@v1")]     // non-numeric version
    [InlineData("1http@1")]     // name starts with digit
    [InlineData("http.foo@1")]  // illegal character in name
    [InlineData("http@1@2")]    // extra @ segments
    [InlineData("")]            // empty
    [InlineData("  ")]          // whitespace
    public void Parse_InvalidIdentifier_Throws(string input)
    {
        var act = () => TaskTypeId.Parse(input);

        act.Should().Throw<FormatException>().WithMessage("*Invalid TaskType*");
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalseAndDefault()
    {
        TaskTypeId.TryParse("bogus", out var result).Should().BeFalse();
        result.Should().Be(default(TaskTypeId));
    }

    [Fact]
    public void TryParse_NullInput_ReturnsFalse()
    {
        TaskTypeId.TryParse(null, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("http@1")]
    [InlineData("default@1")]
    [InlineData("payment-processor@42")]
    public void ToString_RoundTripsThroughParse(string input)
    {
        var id = TaskTypeId.Parse(input);
        id.ToString().Should().Be(input);
    }
}

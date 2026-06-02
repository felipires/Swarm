using FluentAssertions;
using Swarm.Cluster.Validation;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P1-7a Cluster side: the lenient placeholder-aware schema check. The
/// validator must accept configs containing <c>{src:key}</c> at both string
/// and value positions and still verify the post-substitution shape against
/// the handler's JSON Schema.
/// </summary>
public class PlaceholderAwareSchemaValidatorTests
{
    private const string HttpSchema = """
        {
          "type": "object",
          "required": ["method", "url"],
          "properties": {
            "method": { "type": "string" },
            "url": { "type": "string" },
            "timeoutSeconds": { "type": "integer", "minimum": 1 }
          }
        }
        """;

    [Fact]
    public void Substitute_StringPositionPlaceholder_StaysInsideQuotes()
    {
        var prepared = PlaceholderAwareSchemaValidator.SubstitutePlaceholders(
            """{"url": "https://api.example.com/sync/{param:tenant:required}"}""");

        // The result must be valid JSON with the placeholder replaced by an
        // alphanumeric stand-in inside the surrounding string literal.
        prepared.Should().Be(
            $$$"""{"url": "https://api.example.com/sync/{{{PlaceholderAwareSchemaValidator.Sentinel}}}"}""");
    }

    [Fact]
    public void Substitute_ValuePositionIntPlaceholder_ReplacedWithIntegerLiteral()
    {
        var prepared = PlaceholderAwareSchemaValidator.SubstitutePlaceholders(
            """{"limit": {param:limit:type=int:default=1000}}""");

        prepared.Should().Be("""{"limit": 1000}""");
    }

    [Fact]
    public void Substitute_ValuePositionBoolPlaceholder_ReplacedWithBoolLiteral()
    {
        var prepared = PlaceholderAwareSchemaValidator.SubstitutePlaceholders(
            """{"strict": {param:strict:type=bool}}""");

        prepared.Should().Be("""{"strict": false}""");
    }

    [Fact]
    public void Validate_ConfigSatisfyingSchema_ReturnsNoFailures()
    {
        var config = """{"method": "POST", "url": "https://api.example.com"}""";

        var failures = PlaceholderAwareSchemaValidator.Validate(config, HttpSchema);

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ConfigWithPlaceholderInString_PassesAfterSubstitution()
    {
        // A real-world template with placeholders inside string fields.
        var config = """{"method": "POST", "url": "https://{env:HOST}/v1/{param:id:required}"}""";

        var failures = PlaceholderAwareSchemaValidator.Validate(config, HttpSchema);

        failures.Should().BeEmpty(
            "string-position placeholders are substituted in-place; the schema check sees a valid URL string");
    }

    [Fact]
    public void Validate_MissingRequiredField_Fails()
    {
        var config = """{"url": "https://api.example.com"}""";   // no method

        var failures = PlaceholderAwareSchemaValidator.Validate(config, HttpSchema);

        failures.Should().NotBeEmpty();
        failures.Should().Contain(f => f.Message.Contains("method"));
    }

    [Fact]
    public void Validate_WrongFieldType_Fails()
    {
        // timeoutSeconds is supposed to be an integer; passing a string at
        // value position lets the schema check catch the type mismatch
        // before the message hits the broker.
        var config = """{"method": "GET", "url": "https://x", "timeoutSeconds": "thirty"}""";

        var failures = PlaceholderAwareSchemaValidator.Validate(config, HttpSchema);

        failures.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ValuePositionPlaceholderHonorsTypeModifier()
    {
        // timeoutSeconds is integer; type=int placeholder substitutes 0 which
        // satisfies the "integer" type but fails minimum:1. That's the Node's
        // problem (re-validation post-resolution); Cluster says fine.
        var config = """{"method": "GET", "url": "https://x", "timeoutSeconds": {param:t:type=int:default=30}}""";

        var failures = PlaceholderAwareSchemaValidator.Validate(config, HttpSchema);

        failures.Should().BeEmpty();
    }
}

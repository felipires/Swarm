using FluentAssertions;
using Swarm.Sdk.ValueResolution;
using Xunit;

namespace Swarm.Sdk.Tests;

public class ValueResolverPipelineTests
{
    [Fact]
    public async Task InterpolateAsync_ReplacesPlaceholderWithResolvedValue()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("env", "API", "abc123") });

        var result = await pipeline.InterpolateAsync("Bearer {env:API}", default);

        result.Should().Be("Bearer abc123");
    }

    [Fact]
    public async Task InterpolateAsync_MissingRequired_Throws()
    {
        var pipeline = new ValueResolverPipeline(new[] { Empty("env") });

        var act = async () => await pipeline.InterpolateAsync("{env:MISSING:required}", default);

        await act.Should().ThrowAsync<ValueResolutionException>()
            .WithMessage("*Required value missing: env:MISSING*");
    }

    [Fact]
    public async Task InterpolateAsync_DefaultModifier_UsedWhenAbsent()
    {
        var pipeline = new ValueResolverPipeline(new[] { Empty("env") });

        var result = await pipeline.InterpolateAsync("{env:K:default=fallback}", default);

        result.Should().Be("fallback");
    }

    [Fact]
    public async Task InterpolateAsync_AbsentNoModifier_RendersEmptyString()
    {
        var pipeline = new ValueResolverPipeline(new[] { Empty("env") });

        var result = await pipeline.InterpolateAsync("<{env:GONE}>", default);

        result.Should().Be("<>");
    }

    [Fact]
    public async Task InterpolateAsync_SecretModifier_TracksRawForRedaction()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("env", "K", "shhh") });

        await pipeline.InterpolateAsync("{env:K:secret}", default);

        pipeline.Secrets.Should().Contain("shhh");
    }

    [Fact]
    public async Task InterpolateAsync_TypeIntCoercesValid()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("param", "limit", "42") });

        var result = await pipeline.InterpolateAsync("{param:limit:type=int}", default);

        result.Should().Be("42");
    }

    [Fact]
    public async Task InterpolateAsync_TypeIntRejectsNonInteger()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("param", "limit", "abc") });

        var act = async () => await pipeline.InterpolateAsync("{param:limit:type=int}", default);

        await act.Should().ThrowAsync<ValueResolutionException>()
            .WithMessage("*Cannot coerce 'abc' to int*");
    }

    [Fact]
    public async Task InterpolateAsync_UnknownSource_Throws()
    {
        var pipeline = new ValueResolverPipeline(Array.Empty<IValueResolver>());

        var act = async () => await pipeline.InterpolateAsync("{ghost:K}", default);

        await act.Should().ThrowAsync<ValueResolutionException>()
            .WithMessage("*Unknown placeholder source 'ghost'*");
    }

    [Fact]
    public async Task InterpolateAsync_NonPlaceholderBraces_PassThroughVerbatim()
    {
        // Real-world: JSON object braces around the template must be preserved.
        var pipeline = new ValueResolverPipeline(new[] { Static("env", "K", "v") });

        var result = await pipeline.InterpolateAsync("""{"a":"{env:K}","b":{"nested":1}}""", default);

        result.Should().Be("""{"a":"v","b":{"nested":1}}""");
    }

    [Fact]
    public async Task InterpolateSafeAsync_SecretRendersRedactedToken()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("env", "K", "shhh") });

        var result = await pipeline.InterpolateSafeAsync("token={env:K:secret}", default);

        result.Should().Be("token=[REDACTED]");
    }

    [Fact]
    public async Task InterpolateAsync_MultiplePlaceholdersInOneTemplate_AllResolved()
    {
        var pipeline = new ValueResolverPipeline(new[]
        {
            Static("env", "HOST", "api.example.com"),
            Static("param", "ID", "42"),
        });

        var result = await pipeline.InterpolateAsync(
            "https://{env:HOST}/items/{param:ID}", default);

        result.Should().Be("https://api.example.com/items/42");
    }

    [Fact]
    public async Task InterpolateAsync_ValuePositionInt_ProducesUnquotedNumber()
    {
        // Unquoted placeholder at a JSON value position: type=int must emit a
        // bare number so the resolved document is valid JSON with an integer.
        var pipeline = new ValueResolverPipeline(new[] { Static("param", "timeout", "30") });

        var result = await pipeline.InterpolateAsync(
            """{"timeoutSeconds":{param:timeout:type=int}}""", default);

        result.Should().Be("""{"timeoutSeconds":30}""");
        var parsed = System.Text.Json.JsonDocument.Parse(result).RootElement;
        parsed.GetProperty("timeoutSeconds").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Number);
        parsed.GetProperty("timeoutSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task InterpolateAsync_ValuePositionJsonObject_ProducesObject()
    {
        // type=json injects the resolved value verbatim at value position, so a
        // param holding a JSON object lands as a real object, not a string.
        var pipeline = new ValueResolverPipeline(new[]
        {
            Static("param", "headers", """{"Authorization":"Bearer x","Content-Type":"application/json"}"""),
        });

        var result = await pipeline.InterpolateAsync(
            """{"headers":{param:headers:type=json}}""", default);

        var parsed = System.Text.Json.JsonDocument.Parse(result).RootElement;
        var headers = parsed.GetProperty("headers");
        headers.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        headers.GetProperty("Authorization").GetString().Should().Be("Bearer x");
    }

    [Fact]
    public async Task InterpolateAsync_ValuePositionJsonArray_ProducesArray()
    {
        var pipeline = new ValueResolverPipeline(new[] { Static("param", "codes", "[200,201]") });

        var result = await pipeline.InterpolateAsync(
            """{"successStatusCodes":{param:codes:type=json}}""", default);

        var parsed = System.Text.Json.JsonDocument.Parse(result).RootElement;
        var codes = parsed.GetProperty("successStatusCodes");
        codes.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        codes.EnumerateArray().Select(e => e.GetInt32()).Should().Equal(200, 201);
    }

    private static IValueResolver Static(string source, string key, string value)
        => new StubResolver(source, k => k == key ? new ResolvedValue(value) : null);

    private static IValueResolver Empty(string source)
        => new StubResolver(source, _ => null);

    private sealed class StubResolver : IValueResolver
    {
        private readonly Func<string, ResolvedValue?> _lookup;
        public StubResolver(string source, Func<string, ResolvedValue?> lookup)
        {
            Source = source;
            _lookup = lookup;
        }
        public string Source { get; }
        public Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_lookup(key));
    }
}

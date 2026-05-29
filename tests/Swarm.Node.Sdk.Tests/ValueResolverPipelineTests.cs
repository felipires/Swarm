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

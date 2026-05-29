using System.Text.Json;
using FluentAssertions;
using Swarm.Sdk.ValueResolution;
using Swarm.Node.ValueResolution;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// End-to-end check of the three Node-side resolvers feeding the SDK
/// pipeline. Covers the typical handler flow: take the raw config JSON,
/// interpolate, parse the result.
/// </summary>
public class ResolverIntegrationTests : IDisposable
{
    private readonly List<string> _envKeysToClear = new();

    [Fact]
    public async Task EnvStoreResolver_ReadsFromProcessEnvWithPrefix()
    {
        SetEnv("SWARM_TASKENV_API_TOKEN", "abc-123");
        var resolver = new EnvStoreResolver();

        var result = await resolver.ResolveAsync("API_TOKEN", default);

        result.Should().NotBeNull();
        result!.Raw.Should().Be("abc-123");
    }

    [Fact]
    public async Task EnvStoreResolver_AbsentReturnsNull()
    {
        var resolver = new EnvStoreResolver();
        (await resolver.ResolveAsync("DOES_NOT_EXIST_" + Guid.NewGuid(), default)).Should().BeNull();
    }

    [Fact]
    public async Task ParamResolver_DotNotationDescendsNestedObjects()
    {
        var doc = JsonDocument.Parse("""{"address":{"city":"Berlin","zip":"10115"}}""");
        var resolver = new ParamResolver(doc.RootElement);

        (await resolver.ResolveAsync("address.city", default))!.Raw.Should().Be("Berlin");
        (await resolver.ResolveAsync("address.zip", default))!.Raw.Should().Be("10115");
        (await resolver.ResolveAsync("address.missing", default)).Should().BeNull();
    }

    [Fact]
    public async Task Pipeline_HandlerStyleInterpolation_ProducesValidJson()
    {
        SetEnv("SWARM_TASKENV_API_TOKEN", "secret-token");

        var params_ = JsonDocument.Parse("""{"customerId":"acme","since":"2026-01-01"}""");
        var config = JsonDocument.Parse(
            """{"url":"https://api.example.com/sync/{param:customerId}","headers":{"Authorization":"Bearer {env:API_TOKEN:secret}","Since":"{param:since}"}}""");

        var pipeline = new ValueResolverPipeline(new IValueResolver[]
        {
            new EnvStoreResolver(),
            new ParamResolver(params_.RootElement),
            new ConfigResolver(config.RootElement),
        });

        var resolved = await pipeline.InterpolateAsync(config.RootElement.GetRawText(), default);

        var parsed = JsonDocument.Parse(resolved).RootElement;
        parsed.GetProperty("url").GetString().Should().Be("https://api.example.com/sync/acme");
        parsed.GetProperty("headers").GetProperty("Authorization").GetString().Should().Be("Bearer secret-token");
        parsed.GetProperty("headers").GetProperty("Since").GetString().Should().Be("2026-01-01");

        pipeline.Secrets.Should().Contain("secret-token",
            "the :secret modifier must mark the resolved value for downstream redaction");
    }

    private void SetEnv(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _envKeysToClear.Add(key);
    }

    public void Dispose()
    {
        foreach (var k in _envKeysToClear) Environment.SetEnvironmentVariable(k, null);
        GC.SuppressFinalize(this);
    }
}

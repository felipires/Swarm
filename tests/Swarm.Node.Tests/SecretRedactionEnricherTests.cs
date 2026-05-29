using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;
using Swarm.Node.Logging;
using Swarm.Sdk.ValueResolution;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// P4-2a: any structured log property whose ScalarValue&lt;string&gt;
/// contains a secret resolved by the active <see cref="ValueResolverPipeline"/>
/// must be replaced with <c>[REDACTED]</c> before sinks see it.
/// </summary>
public class SecretRedactionEnricherTests
{
    [Fact]
    public void Enrich_ScalarStringContainingSecret_ReplacedWithRedactedToken()
    {
        var pipeline = PipelineWithSecrets("abc-token-123");
        using var scope = SecretRedactionContext.Push(pipeline);

        var logEvent = BuildEvent(new LogEventProperty(
            "Authorization", new ScalarValue("Bearer abc-token-123")));

        new SecretRedactionEnricher().Enrich(logEvent, new TestPropertyFactory());

        Scalar(logEvent, "Authorization").Should().Be("Bearer [REDACTED]");
    }

    [Fact]
    public void Enrich_PropertyWithoutSecret_LeftUntouched()
    {
        var pipeline = PipelineWithSecrets("never-appears");
        using var scope = SecretRedactionContext.Push(pipeline);

        var logEvent = BuildEvent(new LogEventProperty(
            "Method", new ScalarValue("POST")));

        new SecretRedactionEnricher().Enrich(logEvent, new TestPropertyFactory());

        Scalar(logEvent, "Method").Should().Be("POST");
    }

    [Fact]
    public void Enrich_NoActiveContext_NoMutation()
    {
        // Without a pushed pipeline the enricher must be a no-op even if a
        // matching string appears verbatim in a property.
        var logEvent = BuildEvent(new LogEventProperty(
            "Payload", new ScalarValue("Bearer abc-token-123")));

        new SecretRedactionEnricher().Enrich(logEvent, new TestPropertyFactory());

        Scalar(logEvent, "Payload").Should().Be("Bearer abc-token-123");
    }

    [Fact]
    public void Enrich_NestedStructureValue_RedactedRecursively()
    {
        var pipeline = PipelineWithSecrets("inner-secret");
        using var scope = SecretRedactionContext.Push(pipeline);

        var nested = new StructureValue(new[]
        {
            new LogEventProperty("Authorization", new ScalarValue("Bearer inner-secret")),
            new LogEventProperty("X-Tenant", new ScalarValue("acme")),
        });
        var logEvent = BuildEvent(new LogEventProperty("Headers", nested));

        new SecretRedactionEnricher().Enrich(logEvent, new TestPropertyFactory());

        var headers = (StructureValue)logEvent.Properties["Headers"];
        ((ScalarValue)headers.Properties.Single(p => p.Name == "Authorization").Value).Value
            .Should().Be("Bearer [REDACTED]");
        ((ScalarValue)headers.Properties.Single(p => p.Name == "X-Tenant").Value).Value
            .Should().Be("acme");
    }

    [Fact]
    public void Enrich_LiveSecretsCollection_PicksUpLateAdditions()
    {
        // Realistic flow: handler emits a log line, then calls
        // InterpolateAsync which adds a secret to pipeline.Secrets, then
        // emits a second log line. The second event must be redacted using
        // the freshly-added secret.
        var pipeline = new ValueResolverPipeline(new IValueResolver[] { new InlineSecretResolver() });
        using var scope = SecretRedactionContext.Push(pipeline);

        var early = BuildEvent(new LogEventProperty("Token", new ScalarValue("Bearer later-secret")));
        new SecretRedactionEnricher().Enrich(early, new TestPropertyFactory());
        Scalar(early, "Token").Should().Be("Bearer later-secret",
            "no secret has been resolved yet, so redaction is a no-op");

        // Simulate a later InterpolateAsync that resolves a :secret value.
        _ = pipeline.InterpolateAsync("{seed:later-secret:secret}", default).GetAwaiter().GetResult();

        var late = BuildEvent(new LogEventProperty("Token", new ScalarValue("Bearer later-secret")));
        new SecretRedactionEnricher().Enrich(late, new TestPropertyFactory());
        Scalar(late, "Token").Should().Be("Bearer [REDACTED]");
    }

    private static ValueResolverPipeline PipelineWithSecrets(params string[] secrets)
    {
        var pipeline = new ValueResolverPipeline(new IValueResolver[] { new InlineSecretResolver() });
        foreach (var s in secrets)
            _ = pipeline.InterpolateAsync($"{{seed:{s}:secret}}", default).GetAwaiter().GetResult();
        return pipeline;
    }

    private static LogEvent BuildEvent(params LogEventProperty[] properties)
    {
        var template = new MessageTemplateParser().Parse("test");
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            exception: null,
            messageTemplate: template,
            properties: properties);
    }

    private static string Scalar(LogEvent logEvent, string name)
        => (string)((ScalarValue)logEvent.Properties[name]).Value!;

    /// <summary>
    /// Fake source <c>seed</c> that echoes the placeholder key as a secret-flagged
    /// value, so we can populate a pipeline's secret set without poking private state.
    /// </summary>
    private sealed class InlineSecretResolver : IValueResolver
    {
        public string Source => "seed";
        public Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedValue?>(new ResolvedValue(key, IsSecret: true));
    }

    private sealed class TestPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, value is LogEventPropertyValue lepv ? lepv : new ScalarValue(value));
    }
}

using System.Text.Json;
using FluentAssertions;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// P1-9: per-step runtime params let two steps sharing a TaskDefinition be
/// parameterized differently. Covers the dispatch-time merge precedence in
/// <see cref="PipelineRunExecutor.BuildEffectiveParams"/>:
/// run params (base) &lt; step static params &lt; output mappings (live, highest).
/// </summary>
public class StepParamPrecedenceTests
{
    private static PipelineRunExecutor.StepSnapshot Step(
        string name,
        Guid stepId,
        string? runtimeParamsJson = null,
        List<OutputMapping>? outputMappings = null,
        List<Guid>? dependsOn = null)
        => new(
            StepId: stepId,
            Name: name,
            TaskDefinitionId: Guid.NewGuid(),
            DependsOn: dependsOn ?? new List<Guid>(),
            Strategy: null,
            TargetNodeId: null,
            TargetTagsJson: null,
            FailurePolicy: StepFailurePolicy.FailPipeline,
            OutputMappings: outputMappings,
            RuntimeParamsJson: runtimeParamsJson);

    private static PipelineStepInstance Instance(Guid stepId, PipelineStepInstanceStatus status, string? resultJson = null)
        => new()
        {
            Id = Guid.NewGuid(),
            PipelineRunId = Guid.NewGuid(),
            PipelineStepId = stepId,
            Status = status,
            ResultJson = resultJson,
            CreatedAt = DateTime.UtcNow,
        };

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        json.Should().NotBeNull();
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json!)!;
    }

    [Fact]
    public void NoStepParamsOrMappings_ReturnsRunParamsUnchanged()
    {
        var run = """{"shared":"x"}""";
        var step = Step("a", Guid.NewGuid());

        var result = PipelineRunExecutor.BuildEffectiveParams(
            run, step, new() { step }, new());

        result.Should().Be(run);
    }

    [Fact]
    public void StepParams_OverrideRunParams_AndAddNewKeys()
    {
        var run = """{"endpoint":"/default","timeout":30}""";
        var step = Step("a", Guid.NewGuid(), runtimeParamsJson: """{"endpoint":"/users","retries":3}""");

        var merged = Parse(PipelineRunExecutor.BuildEffectiveParams(run, step, new() { step }, new()));

        merged["endpoint"].GetString().Should().Be("/users"); // step wins over run
        merged["timeout"].GetInt32().Should().Be(30);          // run base preserved
        merged["retries"].GetInt32().Should().Be(3);           // step adds new key
    }

    [Fact]
    public void TwoStepsSharingDefinition_GetTheirOwnParams()
    {
        // The flaw this feature fixes: same run params, different per-step values.
        var run = """{"region":"eu"}""";
        var a = Step("a", Guid.NewGuid(), runtimeParamsJson: """{"endpoint":"/users"}""");
        var b = Step("b", Guid.NewGuid(), runtimeParamsJson: """{"endpoint":"/orders"}""");
        var all = new List<PipelineRunExecutor.StepSnapshot> { a, b };

        var ra = Parse(PipelineRunExecutor.BuildEffectiveParams(run, a, all, new()));
        var rb = Parse(PipelineRunExecutor.BuildEffectiveParams(run, b, all, new()));

        ra["endpoint"].GetString().Should().Be("/users");
        rb["endpoint"].GetString().Should().Be("/orders");
        ra["region"].GetString().Should().Be("eu"); // shared run param still applies
        rb["region"].GetString().Should().Be("eu");
    }

    [Fact]
    public void OutputMapping_WinsOverStepParam_AndRunParam()
    {
        var upstreamId = Guid.NewGuid();
        var upstream = Step("extract", upstreamId);
        var downstreamId = Guid.NewGuid();
        var downstream = Step(
            "load",
            downstreamId,
            runtimeParamsJson: """{"record_id":"step-default"}""",
            outputMappings: new() { new OutputMapping("extract", "id", "record_id") },
            dependsOn: new() { upstreamId });

        var all = new List<PipelineRunExecutor.StepSnapshot> { upstream, downstream };
        var instances = new List<PipelineStepInstance>
        {
            Instance(upstreamId, PipelineStepInstanceStatus.Completed, resultJson: """{"id":"mapped-42"}"""),
            Instance(downstreamId, PipelineStepInstanceStatus.Waiting),
        };

        var merged = Parse(PipelineRunExecutor.BuildEffectiveParams(
            """{"record_id":"run-default"}""", downstream, all, instances));

        merged["record_id"].GetString().Should().Be("mapped-42"); // mapping beats step + run
    }

    [Fact]
    public void OutputMapping_FallsBackToStepParam_WhenUpstreamPathMissing()
    {
        var upstreamId = Guid.NewGuid();
        var upstream = Step("extract", upstreamId);
        var downstreamId = Guid.NewGuid();
        var downstream = Step(
            "load",
            downstreamId,
            runtimeParamsJson: """{"record_id":"step-default"}""",
            outputMappings: new() { new OutputMapping("extract", "missing", "record_id") },
            dependsOn: new() { upstreamId });

        var all = new List<PipelineRunExecutor.StepSnapshot> { upstream, downstream };
        var instances = new List<PipelineStepInstance>
        {
            Instance(upstreamId, PipelineStepInstanceStatus.Completed, resultJson: """{"other":"value"}"""),
            Instance(downstreamId, PipelineStepInstanceStatus.Waiting),
        };

        var merged = Parse(PipelineRunExecutor.BuildEffectiveParams(
            null, downstream, all, instances));

        // Path not found → mapping skipped → step static param stands.
        merged["record_id"].GetString().Should().Be("step-default");
    }

    [Fact]
    public void OutputMapping_SkippedWhenUpstreamNotCompleted()
    {
        var upstreamId = Guid.NewGuid();
        var upstream = Step("extract", upstreamId);
        var downstreamId = Guid.NewGuid();
        var downstream = Step(
            "load",
            downstreamId,
            runtimeParamsJson: """{"record_id":"step-default"}""",
            outputMappings: new() { new OutputMapping("extract", "id", "record_id") },
            dependsOn: new() { upstreamId });

        var all = new List<PipelineRunExecutor.StepSnapshot> { upstream, downstream };
        var instances = new List<PipelineStepInstance>
        {
            Instance(upstreamId, PipelineStepInstanceStatus.Failed, resultJson: """{"id":"mapped-42"}"""),
            Instance(downstreamId, PipelineStepInstanceStatus.Waiting),
        };

        var merged = Parse(PipelineRunExecutor.BuildEffectiveParams(
            null, downstream, all, instances));

        merged["record_id"].GetString().Should().Be("step-default");
    }

    [Fact]
    public void EmptyRunParams_WithStepParams_ProducesStepParams()
    {
        var step = Step("a", Guid.NewGuid(), runtimeParamsJson: """{"endpoint":"/users"}""");

        var merged = Parse(PipelineRunExecutor.BuildEffectiveParams(null, step, new() { step }, new()));

        merged.Should().ContainKey("endpoint");
        merged["endpoint"].GetString().Should().Be("/users");
    }
}

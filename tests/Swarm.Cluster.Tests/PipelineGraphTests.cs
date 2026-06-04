using FluentAssertions;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services.Pipelines;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Pure DAG logic (no DB) — exhaustive coverage of the structural
/// invariants <see cref="PipelineGraph"/> enforces.
/// </summary>
public class PipelineGraphTests
{
    [Fact]
    public void Build_EmptyStepList_Throws()
    {
        var act = () => PipelineGraph.Build(Array.Empty<PipelineStep>());

        act.Should().Throw<PipelineGraphException>()
            .Where(e => e.Code == "EMPTY_PIPELINE");
    }

    [Fact]
    public void Build_DuplicateStepNames_Throws()
    {
        var steps = new[]
        {
            Step("extract"),
            Step("EXTRACT"),  // case-insensitive collision
        };

        var act = () => PipelineGraph.Build(steps);

        act.Should().Throw<PipelineGraphException>()
            .Where(e => e.Code == "DUPLICATE_STEP_NAME");
    }

    [Fact]
    public void Build_SelfLoop_Throws()
    {
        var self = Guid.NewGuid();
        var steps = new[] { Step("loop", id: self, deps: new[] { self }) };

        var act = () => PipelineGraph.Build(steps);

        act.Should().Throw<PipelineGraphException>().Where(e => e.Code == "SELF_LOOP");
    }

    [Fact]
    public void Build_DanglingDependency_Throws()
    {
        var bogus = Guid.NewGuid();
        var steps = new[] { Step("only", deps: new[] { bogus }) };

        var act = () => PipelineGraph.Build(steps);

        act.Should().Throw<PipelineGraphException>().Where(e => e.Code == "DANGLING_DEPENDENCY");
    }

    [Fact]
    public void Build_Cycle_Throws()
    {
        // a → b → c → a
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var steps = new[]
        {
            Step("a", id: a, deps: new[] { c }),
            Step("b", id: b, deps: new[] { a }),
            Step("c", id: c, deps: new[] { b }),
        };

        var act = () => PipelineGraph.Build(steps);

        act.Should().Throw<PipelineGraphException>().Where(e => e.Code == "CYCLE_DETECTED");
    }

    [Fact]
    public void TopologicalOrder_RespectsDependencies()
    {
        // a → b → c, a → d
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var steps = new[]
        {
            Step("a", id: a),
            Step("b", id: b, deps: new[] { a }),
            Step("c", id: c, deps: new[] { b }),
            Step("d", id: d, deps: new[] { a }),
        };

        var graph = PipelineGraph.Build(steps);
        var positions = graph.TopologicalOrder
            .Select((id, idx) => (id, idx))
            .ToDictionary(p => p.id, p => p.idx);

        positions.Should().HaveCount(4);
        positions[a].Should().BeLessThan(positions[b]);
        positions[b].Should().BeLessThan(positions[c]);
        positions[a].Should().BeLessThan(positions[d]);
    }

    [Fact]
    public void RootIds_AreStepsWithNoDependencies()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var graph = PipelineGraph.Build(new[]
        {
            Step("a", id: a),
            Step("b", id: b),                       // also a root
            Step("c", id: c, deps: new[] { a, b }),
        });

        graph.RootIds().Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void ResolveNextReady_OnlyReturnsWaitingStepsWithAllDepsCompleted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var graph = PipelineGraph.Build(new[]
        {
            Step("a", id: a),
            Step("b", id: b, deps: new[] { a }),
            Step("c", id: c, deps: new[] { a, b }),
        });

        // a completed; b still waiting (its dep is done) → b ready, c not yet.
        var state1 = new Dictionary<Guid, PipelineStepInstanceStatus>
        {
            [a] = PipelineStepInstanceStatus.Completed,
            [b] = PipelineStepInstanceStatus.Waiting,
            [c] = PipelineStepInstanceStatus.Waiting,
        };
        graph.ResolveNextReady(state1).Should().BeEquivalentTo(new[] { b });

        // a + b completed → c ready.
        var state2 = new Dictionary<Guid, PipelineStepInstanceStatus>
        {
            [a] = PipelineStepInstanceStatus.Completed,
            [b] = PipelineStepInstanceStatus.Completed,
            [c] = PipelineStepInstanceStatus.Waiting,
        };
        graph.ResolveNextReady(state2).Should().BeEquivalentTo(new[] { c });
    }

    [Fact]
    public void ResolveNextReady_FailedDependency_DoesNotUnblock()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var graph = PipelineGraph.Build(new[]
        {
            Step("a", id: a),
            Step("b", id: b, deps: new[] { a }),
        });

        var state = new Dictionary<Guid, PipelineStepInstanceStatus>
        {
            [a] = PipelineStepInstanceStatus.Failed,
            [b] = PipelineStepInstanceStatus.Waiting,
        };

        graph.ResolveNextReady(state).Should().BeEmpty(
            "a Failed dependency must not unblock its dependent — Completed is the only allowed pre-state");
    }

    [Fact]
    public void Descendants_WalksTransitiveClosure()
    {
        // a → b → d, a → c
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        var graph = PipelineGraph.Build(new[]
        {
            Step("a", id: a),
            Step("b", id: b, deps: new[] { a }),
            Step("c", id: c, deps: new[] { a }),
            Step("d", id: d, deps: new[] { b }),
        });

        graph.Descendants(a).Should().BeEquivalentTo(new[] { b, c, d });
        graph.Descendants(b).Should().BeEquivalentTo(new[] { d });
        graph.Descendants(d).Should().BeEmpty();
    }

    [Fact]
    public void TryResolveByName_IsCaseInsensitive()
    {
        var stepId = Guid.NewGuid();
        var graph = PipelineGraph.Build(new[] { Step("extract", id: stepId) });

        graph.TryResolveByName("EXTRACT", out var found).Should().BeTrue();
        found.Should().Be(stepId);
    }

    private static PipelineStep Step(string name, Guid? id = null, IEnumerable<Guid>? deps = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            DependsOnJson = PipelineGraph.DependencyDecoder.Encode(deps ?? Array.Empty<Guid>()),
        };
}

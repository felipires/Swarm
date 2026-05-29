using FluentAssertions;
using Swarm.Node.Sdk.Abstractions;
using Swarm.Node.Services;
using Xunit;

namespace Swarm.Node.Tests;

public class HandlerRegistryTests
{
    [Fact]
    public void Constructor_DuplicateTaskType_Throws()
    {
        var handlers = new ITaskHandler[]
        {
            new FakeHandler("x@1"),
            new FakeHandler("x@1"),
        };

        var act = () => new HandlerRegistry(handlers);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate ITaskHandler registration for TaskType 'x@1'*");
    }

    [Fact]
    public void RegisteredTaskTypes_ReportsEveryRegisteredHandler()
    {
        var registry = new HandlerRegistry(new ITaskHandler[]
        {
            new FakeHandler("a@1"),
            new FakeHandler("b@1"),
        });

        registry.RegisteredTaskTypes.Should().BeEquivalentTo(new[] { "a@1", "b@1" });
    }

    [Fact]
    public void TryGet_KnownTaskType_ReturnsHandler()
    {
        var b = new FakeHandler("b@1");
        var registry = new HandlerRegistry(new ITaskHandler[] { new FakeHandler("a@1"), b });

        registry.TryGet("b@1", out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(b);
    }

    [Fact]
    public void TryGet_UnknownTaskType_ReturnsFalse()
    {
        var registry = new HandlerRegistry(new ITaskHandler[] { new FakeHandler("a@1") });

        registry.TryGet("missing@1", out var resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Fact]
    public void TryGet_IsCaseSensitive_PerD3()
    {
        // Per roadmap D3, TaskType identifiers are exact-match strings.
        var registry = new HandlerRegistry(new ITaskHandler[] { new FakeHandler("http@1") });

        registry.TryGet("HTTP@1", out _).Should().BeFalse();
    }

    private sealed class FakeHandler(string taskType) : ITaskHandler
    {
        public string TaskType { get; } = taskType;
        public HandlerSchema Schema { get; } = new();
        public Task<TaskResult> HandleAsync(TaskContext context)
            => Task.FromResult(new TaskResult(true));
    }
}

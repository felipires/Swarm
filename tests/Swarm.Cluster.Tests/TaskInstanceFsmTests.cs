using FluentAssertions;
using Swarm.Cluster.Models;
using Xunit;
using static Swarm.Cluster.Models.TaskInstance;

namespace Swarm.Cluster.Tests;

public class TaskInstanceFsmTests
{
    [Fact]
    public void New_Instance_StartsPending()
    {
        new TaskInstance().Status.Should().Be(TaskInstanceStatus.Pending);
    }

    [Theory]
    [InlineData(TaskInstanceStatus.Pending, TaskInstanceStatus.Dispatched)]
    [InlineData(TaskInstanceStatus.Pending, TaskInstanceStatus.Claimed)]
    [InlineData(TaskInstanceStatus.Pending, TaskInstanceStatus.Failed)]
    [InlineData(TaskInstanceStatus.Claimed, TaskInstanceStatus.Running)]
    [InlineData(TaskInstanceStatus.Claimed, TaskInstanceStatus.Failed)]
    [InlineData(TaskInstanceStatus.Dispatched, TaskInstanceStatus.Running)]
    [InlineData(TaskInstanceStatus.Dispatched, TaskInstanceStatus.Completed)]
    [InlineData(TaskInstanceStatus.Dispatched, TaskInstanceStatus.Failed)]
    [InlineData(TaskInstanceStatus.Running, TaskInstanceStatus.Completed)]
    [InlineData(TaskInstanceStatus.Running, TaskInstanceStatus.Failed)]
    [InlineData(TaskInstanceStatus.Failed, TaskInstanceStatus.Pending)]   // retry
    public void Transition_AllowedEdge_Succeeds(TaskInstanceStatus from, TaskInstanceStatus to)
    {
        var instance = BuildAt(from);

        instance.Transition(to);

        instance.Status.Should().Be(to);
    }

    [Theory]
    [InlineData(TaskInstanceStatus.Completed, TaskInstanceStatus.Failed)]
    [InlineData(TaskInstanceStatus.Completed, TaskInstanceStatus.Running)]
    [InlineData(TaskInstanceStatus.Completed, TaskInstanceStatus.Pending)]
    [InlineData(TaskInstanceStatus.Failed, TaskInstanceStatus.Completed)]
    [InlineData(TaskInstanceStatus.Pending, TaskInstanceStatus.Running)]   // must go via Dispatched/Claimed
    [InlineData(TaskInstanceStatus.Pending, TaskInstanceStatus.Completed)]
    // Dispatched → Pending and Running → Pending are NOW allowed (P1-2
    // retry path), so they were removed from this forbidden-edge sweep.
    public void Transition_ForbiddenEdge_Throws(TaskInstanceStatus from, TaskInstanceStatus to)
    {
        var instance = BuildAt(from);

        var act = () => instance.Transition(to);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Invalid transition {from} → {to}*");
    }

    [Fact]
    public void Transition_FromCompleted_AlwaysThrows()
    {
        var instance = BuildAt(TaskInstanceStatus.Completed);

        foreach (var target in Enum.GetValues<TaskInstanceStatus>())
        {
            var act = () => instance.Transition(target);
            act.Should().Throw<InvalidOperationException>(
                $"Completed is terminal; transition to {target} must be rejected");
        }
    }

    private static TaskInstance BuildAt(TaskInstanceStatus state)
    {
        // Drive the FSM to the desired starting state via valid transitions so
        // tests never touch the private setter via reflection.
        var instance = new TaskInstance { Id = Guid.NewGuid() };
        var path = state switch
        {
            TaskInstanceStatus.Pending    => Array.Empty<TaskInstanceStatus>(),
            TaskInstanceStatus.Claimed    => [TaskInstanceStatus.Claimed],
            TaskInstanceStatus.Dispatched => [TaskInstanceStatus.Dispatched],
            TaskInstanceStatus.Running    => [TaskInstanceStatus.Dispatched, TaskInstanceStatus.Running],
            TaskInstanceStatus.Completed  => [TaskInstanceStatus.Dispatched, TaskInstanceStatus.Completed],
            TaskInstanceStatus.Failed     => new[] { TaskInstanceStatus.Failed },
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        foreach (var s in path) instance.Transition(s);
        return instance;
    }
}

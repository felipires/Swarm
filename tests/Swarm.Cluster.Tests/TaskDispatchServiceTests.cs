using FluentAssertions;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

public class TaskDispatchServiceTests
{
    [Fact]
    public void BuildMessage_ReadsTaskTypeFromInstanceSnapshot()
    {
        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            TaskType = "http@1",
            ConfigJsonSnapshot = """{"url":"https://example.com"}""",
        };

        var message = TaskDispatchService.BuildMessage(instance);

        message.TaskType.Should().Be("http@1");
        message.InstanceId.Should().Be(instance.Id);
        message.TaskDefinitionId.Should().Be(instance.TaskDefinitionId);
        message.NodeId.Should().Be(instance.NodeId);
        message.ConfigJson.Should().Be(instance.ConfigJsonSnapshot);
    }

    [Fact]
    public void BuildMessage_DefaultsTaskTypeToDefaultAt1WhenInstanceUnpopulated()
    {
        // Fresh-constructed TaskInstance carries the snapshot defaults
        // (TaskType = "default@1", ConfigJsonSnapshot = "{}").
        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
        };

        var message = TaskDispatchService.BuildMessage(instance);

        message.TaskType.Should().Be("default@1");
        message.ConfigJson.Should().Be("{}");
    }

    [Fact]
    public void BuildMessage_PreservesConfigSnapshotAndNullableNodeId()
    {
        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = Guid.NewGuid(),
            NodeId = null, // shared-queue dispatch — NodeId nullable until claim
            TaskType = "sql@2",
            ConfigJsonSnapshot = """{"query":"SELECT 1"}""",
            RuntimeParamsJson = """{"tenant":"acme"}""",
        };

        var message = TaskDispatchService.BuildMessage(instance);

        message.ConfigJson.Should().Be("""{"query":"SELECT 1"}""");
        message.NodeId.Should().BeNull();
        message.RuntimeParamsJson.Should().Be("""{"tenant":"acme"}""");
    }
}

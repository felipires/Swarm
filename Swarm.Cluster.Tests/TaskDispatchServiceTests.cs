using FluentAssertions;
using Swarm.Cluster.Models;
using Swarm.Cluster.Services;
using Xunit;

namespace Swarm.Cluster.Tests;

public class TaskDispatchServiceTests
{
    [Fact]
    public void BuildMessage_PropagatesTaskTypeFromDefinition()
    {
        var definition = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "http-sync",
            TaskType = "http@1",
            ConfigJson = """{"url":"https://example.com"}""",
        };
        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = definition.Id,
            NodeId = Guid.NewGuid(),
        };

        var message = TaskDispatchService.BuildMessage(instance, definition);

        message.TaskType.Should().Be("http@1");
        message.InstanceId.Should().Be(instance.Id);
        message.TaskDefinitionId.Should().Be(definition.Id);
        message.NodeId.Should().Be(instance.NodeId);
        message.ConfigJson.Should().Be(definition.ConfigJson);
    }

    [Fact]
    public void BuildMessage_NewTaskDefinition_DefaultsToDefaultAt1()
    {
        // A freshly-constructed TaskDefinition carries the model default per D3.
        var definition = new TaskDefinition { Id = Guid.NewGuid(), Name = "x" };
        var instance = new TaskInstance { Id = Guid.NewGuid(), TaskDefinitionId = definition.Id, NodeId = Guid.NewGuid() };

        var message = TaskDispatchService.BuildMessage(instance, definition);

        message.TaskType.Should().Be("default@1");
    }

    [Fact]
    public void BuildMessage_PreservesConfigJsonAndNodeId()
    {
        var nodeId = Guid.NewGuid();
        var definition = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "sql-extract",
            TaskType = "sql@2",
            ConfigJson = """{"query":"SELECT 1"}""",
        };
        var instance = new TaskInstance
        {
            Id = Guid.NewGuid(),
            TaskDefinitionId = definition.Id,
            NodeId = nodeId,
        };

        var message = TaskDispatchService.BuildMessage(instance, definition);

        message.ConfigJson.Should().Be("""{"query":"SELECT 1"}""");
        message.NodeId.Should().Be(nodeId);
        message.TaskType.Should().Be("sql@2");
    }
}

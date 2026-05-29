using System.Text.Json;
using FluentAssertions;
using Swarm.Node.Sdk.Wire;
using Xunit;

namespace Swarm.Node.Sdk.Tests;

public class TaskMessageSerializationTests
{
    // Cluster publish path and Node consume path both call
    // JsonSerializer.Serialize/Deserialize with NO options object, so the
    // wire shape is PascalCase by default. These tests exercise that exact
    // path — keep them option-free or they stop reflecting production.

    [Fact]
    public void Deserialize_PayloadWithoutTaskType_DefaultsToDefaultAt1()
    {
        // Payload as produced by a pre-versioning Cluster build —
        // no TaskType field present. Backward-compat guarantee per roadmap D3.
        var legacyJson = """
            {
              "InstanceId": "11111111-1111-1111-1111-111111111111",
              "TaskDefinitionId": "22222222-2222-2222-2222-222222222222",
              "NodeId": "33333333-3333-3333-3333-333333333333",
              "ConfigJson": "{}"
            }
            """;

        var message = JsonSerializer.Deserialize<TaskMessage>(legacyJson);

        message.Should().NotBeNull();
        message!.TaskType.Should().Be("default@1");
        message.InstanceId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    [Fact]
    public void RoundTrip_PreservesAllFieldsIncludingNullableNodeId()
    {
        var original = new TaskMessage
        {
            InstanceId = Guid.NewGuid(),
            TaskDefinitionId = Guid.NewGuid(),
            NodeId = null,
            TaskType = "http@2",
            ConfigJson = """{"url":"https://example.com"}""",
            RuntimeParamsJson = """{"tenantId":"acme"}""",
        };

        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<TaskMessage>(json);

        round.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void RoundTrip_WithExplicitNodeId_PreservesNodeId()
    {
        var nodeId = Guid.NewGuid();
        var original = new TaskMessage
        {
            InstanceId = Guid.NewGuid(),
            TaskDefinitionId = Guid.NewGuid(),
            NodeId = nodeId,
            TaskType = "default@1",
        };

        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<TaskMessage>(json);

        round!.NodeId.Should().Be(nodeId);
    }

    [Fact]
    public void Serialize_EmitsPascalCasePropertyNames()
    {
        var message = new TaskMessage
        {
            InstanceId = Guid.Empty,
            TaskDefinitionId = Guid.Empty,
            TaskType = "http@1",
        };

        var json = JsonSerializer.Serialize(message);

        // Wire-format guarantee: PascalCase. Cluster and Node both rely on it.
        json.Should().Contain("\"InstanceId\"")
             .And.Contain("\"TaskType\":\"http@1\"");
    }
}

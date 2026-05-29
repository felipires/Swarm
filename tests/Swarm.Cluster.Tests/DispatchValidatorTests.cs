using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Cluster.Validation;
using Xunit;

namespace Swarm.Cluster.Tests;

public class DispatchValidatorTests
{
    [Fact]
    public async Task Validate_UnsupportedTaskType_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "missing@1", configJson: "{}");
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, null, DispatchStrategy.AnyOnlineNode, null, default);

        var ex = (await act.Should().ThrowAsync<DispatchValidationException>()).Which;
        ex.Code.Should().Be("UNSUPPORTED_TASK_TYPE");
    }

    [Fact]
    public async Task Validate_InvalidConfigJson_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1", configJson: "{not valid");
        await SeedCapableNodeAsync(db, "http@1");
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, null, DispatchStrategy.AnyOnlineNode, null, default);

        (await act.Should().ThrowAsync<DispatchValidationException>()).Which.Code.Should().Be("INVALID_CONFIG_JSON");
    }

    [Fact]
    public async Task Validate_InvalidParamsJson_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1", configJson: "{}");
        await SeedCapableNodeAsync(db, "http@1");
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, "{not valid", DispatchStrategy.AnyOnlineNode, null, default);

        (await act.Should().ThrowAsync<DispatchValidationException>()).Which.Code.Should().Be("INVALID_PARAMS_JSON");
    }

    [Fact]
    public async Task Validate_RequiredPlaceholderParamMissing_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1",
            configJson: """{"url":"https://example.com/{param:tenantId:required}"}""");
        await SeedCapableNodeAsync(db, "http@1");
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, runtimeParamsJson: """{"other":"x"}""", DispatchStrategy.AnyOnlineNode, null, default);

        var ex = (await act.Should().ThrowAsync<DispatchValidationException>()).Which;
        ex.Code.Should().Be("MISSING_REQUIRED_PARAMS");
        ex.Message.Should().Contain("tenantId");
    }

    [Fact]
    public async Task Validate_RequiredPlaceholderParamSupplied_Passes()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1",
            configJson: """{"url":"https://example.com/{param:tenantId:required}"}""");
        await SeedCapableNodeAsync(db, "http@1");
        var validator = new DispatchValidator(db);

        await validator.ValidateAsync(def, runtimeParamsJson: """{"tenantId":"acme"}""", DispatchStrategy.AnyOnlineNode, null, default);
    }

    [Fact]
    public async Task Validate_HandlerDeclaredRequiredParamMissing_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1", configJson: """{"url":"https://example.com"}""");
        await SeedCapableNodeAsync(db, "http@1", requiredParams: new[] { "tenantId" });
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, runtimeParamsJson: """{}""", DispatchStrategy.AnyOnlineNode, null, default);

        var ex = (await act.Should().ThrowAsync<DispatchValidationException>()).Which;
        ex.Code.Should().Be("MISSING_REQUIRED_PARAMS");
        ex.Message.Should().Contain("tenantId");
    }

    [Fact]
    public async Task Validate_EnvKeyDeclaredButNotInConfig_Throws()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1", configJson: """{"url":"https://example.com"}""");
        await SeedCapableNodeAsync(db, "http@1", requiredEnv: new[] { "API_TOKEN" });
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, null, DispatchStrategy.AnyOnlineNode, null, default);

        var ex = (await act.Should().ThrowAsync<DispatchValidationException>()).Which;
        ex.Code.Should().Be("MISSING_REQUIRED_ENV_DECLARATION");
    }

    [Fact]
    public async Task Validate_SpecificNode_OnlyConsidersTargetNode()
    {
        using var db = BuildDb();
        var def = await SeedDefinitionAsync(db, "http@1", configJson: "{}");
        // One capable Node, but not the target.
        await SeedCapableNodeAsync(db, "http@1");
        var otherNode = new Node { Id = Guid.NewGuid(), Name = "other", Status = Node.NodeStatus.Online };
        db.Nodes.Add(otherNode);
        await db.SaveChangesAsync();
        var validator = new DispatchValidator(db);

        var act = async () => await validator.ValidateAsync(def, null, DispatchStrategy.SpecificNode, targetNodeId: otherNode.Id, default);

        (await act.Should().ThrowAsync<DispatchValidationException>()).Which.Code.Should().Be("UNSUPPORTED_TASK_TYPE");
    }

    private static async Task<TaskDefinition> SeedDefinitionAsync(ClusterDbContext db, string taskType, string configJson)
    {
        var def = new TaskDefinition
        {
            Id = Guid.NewGuid(),
            Name = "t",
            TaskType = taskType,
            ConfigJson = configJson,
        };
        db.TaskDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def;
    }

    private static async Task<Node> SeedCapableNodeAsync(
        ClusterDbContext db,
        string taskType,
        string[]? requiredParams = null,
        string[]? requiredEnv = null)
    {
        var node = new Node { Id = Guid.NewGuid(), Name = "n", Status = Node.NodeStatus.Online };
        db.Nodes.Add(node);
        db.NodeCapabilities.Add(new NodeCapability
        {
            Id = Guid.NewGuid(),
            NodeId = node.Id,
            TaskType = taskType,
            RequiredParamsJson = JsonSerializer.Serialize(requiredParams ?? Array.Empty<string>()),
            RequiredEnvKeysJson = JsonSerializer.Serialize(requiredEnv ?? Array.Empty<string>()),
        });
        await db.SaveChangesAsync();
        return node;
    }

    private static ClusterDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ClusterDbContext>()
            .UseInMemoryDatabase($"validator-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClusterDbContext(opts);
    }
}

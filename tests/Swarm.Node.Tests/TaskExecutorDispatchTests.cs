using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Swarm.Node.Data;
using Swarm.Sdk.Abstractions;
using Swarm.Sdk.Wire;
using Swarm.Node.Services;
using Xunit;

namespace Swarm.Node.Tests;

/// <summary>
/// Exercises <see cref="TaskExecutorService.DispatchAsync"/> end-to-end
/// (parse → registry lookup → handler invocation → result envelope) without
/// any broker or SQLite I/O. The DB connection is constructed but never opened
/// because <c>DispatchAsync</c> does not touch it.
/// </summary>
public class TaskExecutorDispatchTests
{
    [Fact]
    public async Task DispatchAsync_KnownTaskType_RoutesToCorrectHandler()
    {
        var a = new RecordingHandler("a@1", new TaskResult(true, ResultJson: "\"from-a\""));
        var b = new RecordingHandler("b@1", new TaskResult(true, ResultJson: "\"from-b\""));
        var executor = BuildExecutor(a, b);

        var result = await executor.DispatchAsync(MessageWith("b@1"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ResultJson.Should().Be("\"from-b\"");
        b.Calls.Should().Be(1);
        a.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_UnknownTaskType_ReturnsUnsupportedError()
    {
        var executor = BuildExecutor(new RecordingHandler("a@1", new TaskResult(true)));

        var result = await executor.DispatchAsync(MessageWith("missing@1"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("UNSUPPORTED_TASK_TYPE:");
        result.ErrorMessage.Should().Contain("missing@1");
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("Http@1")]
    [InlineData("http@0")]
    [InlineData("")]
    public async Task DispatchAsync_MalformedTaskType_ReturnsInvalidError(string badType)
    {
        var executor = BuildExecutor(new RecordingHandler("a@1", new TaskResult(true)));

        var result = await executor.DispatchAsync(MessageWith(badType), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("INVALID_TASKTYPE:");
    }

    [Fact]
    public async Task DispatchAsync_MalformedConfigJson_ReturnsInvalidConfigError()
    {
        var executor = BuildExecutor(new RecordingHandler("a@1", new TaskResult(true)));

        var result = await executor.DispatchAsync(MessageWith("a@1", configJson: "{not valid"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("INVALID_CONFIG_JSON:");
    }

    [Fact]
    public async Task DispatchAsync_PassesParsedConfigAndParamsToHandler()
    {
        RecordingHandler handler = new("a@1", new TaskResult(true));
        var executor = BuildExecutor(handler);

        var message = new TaskMessage
        {
            InstanceId = Guid.NewGuid(),
            TaskType = "a@1",
            ConfigJson = """{"url":"https://example.com"}""",
            RuntimeParamsJson = """{"tenantId":"acme"}""",
        };

        await executor.DispatchAsync(message, CancellationToken.None);

        handler.LastContext.Should().NotBeNull();
        handler.LastContext!.Config.GetProperty("url").GetString().Should().Be("https://example.com");
        handler.LastContext.RuntimeParams.GetProperty("tenantId").GetString().Should().Be("acme");
    }

    [Fact]
    public async Task DispatchAsync_ValuePositionPlaceholders_ResolveToTypedJson()
    {
        // A template with unquoted value-position placeholders is not valid JSON
        // as written; it must still reach the handler, which interpolates
        // RawConfig into a fully-typed, valid resolved config.
        var handler = new RecordingHandler("a@1", new TaskResult(true));
        var executor = BuildExecutor(handler);

        var message = new TaskMessage
        {
            InstanceId = Guid.NewGuid(),
            TaskType = "a@1",
            ConfigJson = """
                {
                  "url": "{param:url}",
                  "headers": {param:headers:type=json},
                  "timeoutSeconds": {param:timeout:type=int},
                  "successStatusCodes": {param:codes:type=json}
                }
                """,
            RuntimeParamsJson = """
                {"url":"https://x.test","headers":{"X-A":"1"},"timeout":30,"codes":[200,201]}
                """,
        };

        var result = await executor.DispatchAsync(message, CancellationToken.None);
        result.Success.Should().BeTrue();

        // The core resolves the config and hands the handler a finished,
        // correctly-typed Config — no per-handler interpolation needed.
        var parsed = handler.LastContext!.Config;
        parsed.GetProperty("url").GetString().Should().Be("https://x.test");
        parsed.GetProperty("headers").ValueKind.Should().Be(JsonValueKind.Object);
        parsed.GetProperty("headers").GetProperty("X-A").GetString().Should().Be("1");
        parsed.GetProperty("timeoutSeconds").ValueKind.Should().Be(JsonValueKind.Number);
        parsed.GetProperty("timeoutSeconds").GetInt32().Should().Be(30);
        parsed.GetProperty("successStatusCodes").EnumerateArray()
            .Select(e => e.GetInt32()).Should().Equal(200, 201);
    }

    private static TaskMessage MessageWith(string taskType, string? configJson = null) => new()
    {
        InstanceId = Guid.NewGuid(),
        TaskType = taskType,
        ConfigJson = configJson ?? "{}",
    };

    private static TaskExecutorService BuildExecutor(params ITaskHandler[] handlers)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NodeId"] = Guid.Empty.ToString(),
                ["Database:ConnectionString"] = "Data Source=:memory:",
            })
            .Build();

        var dataOptions = Options.Create(new DataConfiguration { ConnectionString = "Data Source=:memory:" });
        var db = new AppDbConnection(dataOptions, NullLogger<AppDbConnection>.Instance);
        var envSecrets = new EnvSecretsStore(db, config, NullLogger<EnvSecretsStore>.Instance);

        var plaintextConfig = new PlaintextConfigStore(db);
        return new TaskExecutorService(
            db,
            envSecrets,
            plaintextConfig,
            config,
            NullLogger<TaskExecutorService>.Instance,
            NullLoggerFactory.Instance,
            new HandlerRegistry(handlers));
    }

    private sealed class RecordingHandler(string taskType, TaskResult result) : ITaskHandler
    {
        public string TaskType { get; } = taskType;
        public HandlerSchema Schema { get; } = new();
        public int Calls { get; private set; }
        public TaskContext? LastContext { get; private set; }

        public Task<TaskResult> HandleAsync(TaskContext context)
        {
            Calls++;
            LastContext = context;
            return Task.FromResult(result);
        }
    }
}

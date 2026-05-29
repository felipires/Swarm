using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Handlers;

/// <summary>
/// Built-in handler that acknowledges every task with a success result.
/// Acts as the zero-config fallback so a freshly-deployed Node can satisfy
/// the existing dispatch flow before any product handlers are registered.
/// </summary>
public sealed class DefaultPassthroughHandler : ITaskHandler
{
    public string TaskType => "default@1";
    public HandlerSchema Schema { get; } = new();

    public Task<TaskResult> HandleAsync(TaskContext context)
    {
        context.Logger.LogInformation(
            "DefaultPassthrough executed for {InstanceId}", context.Message.InstanceId);

        var payload = JsonSerializer.Serialize(new
        {
            executed = true,
            instanceId = context.Message.InstanceId,
        });

        return Task.FromResult(new TaskResult(Success: true, ResultJson: payload));
    }
}

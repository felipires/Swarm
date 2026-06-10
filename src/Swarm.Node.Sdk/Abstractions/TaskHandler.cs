using System.Text.Json;

namespace Swarm.Sdk.Abstractions;

/// <summary>
/// Convenience base for handlers whose config is a strongly-typed shape. The
/// Node core resolves all placeholders and hands over <see cref="TaskContext.Config"/>;
/// this base deserializes it into <typeparamref name="TConfig"/> exactly once and
/// eager-returns <c>CONFIG_INVALID</c> if the resolved JSON doesn't fit. Concrete
/// handlers receive a ready-to-use config and never repeat the parse/try-catch.
///
/// Handlers that don't want a typed config (e.g. a passthrough) can implement
/// <see cref="ITaskHandler"/> directly instead.
/// </summary>
/// <typeparam name="TConfig">The handler's config DTO.</typeparam>
public abstract class TaskHandler<TConfig> : ITaskHandler where TConfig : class
{
    public abstract string TaskType { get; }

    public abstract HandlerSchema Schema { get; }

    /// <summary>
    /// Override to customize how <see cref="TaskContext.Config"/> is deserialized.
    /// Defaults to case-insensitive property matching.
    /// </summary>
    protected virtual JsonSerializerOptions? ConfigJsonOptions => null;

    /// <inheritdoc/>
    public Task<TaskResult> HandleAsync(TaskContext context)
    {
        TConfig? config;
        try
        {
            config = context.GetConfig<TConfig>(ConfigJsonOptions);
        }
        catch (JsonException ex)
        {
            return Task.FromResult(new TaskResult(false, ErrorMessage: $"CONFIG_INVALID: {ex.Message}"));
        }

        if (config is null)
            return Task.FromResult(new TaskResult(false, ErrorMessage: "CONFIG_INVALID: config is empty"));

        return HandleAsync(config, context);
    }

    /// <summary>
    /// Execute the task with the resolved, deserialized config. Throwing is fine —
    /// the Node core converts an unhandled exception into a failed result — but
    /// returning a typed <see cref="TaskResult"/> with a specific error code is
    /// preferred for expected failures.
    /// </summary>
    protected abstract Task<TaskResult> HandleAsync(TConfig config, TaskContext context);
}

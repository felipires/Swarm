namespace Swarm.Sdk.Abstractions;

/// <summary>
/// The primary integration surface for Swarm. Implement this on a class,
/// register it via <c>services.AddTaskHandler&lt;T&gt;()</c>, and ship the
/// assembly inside a Node container.
/// </summary>
public interface ITaskHandler
{
    /// <summary>
    /// Identifier of the task type this handler claims, including version
    /// (e.g. <c>"http@1"</c>). The version suffix is mandatory; schema changes
    /// mean a new version, never a mutation of an existing one.
    /// </summary>
    string TaskType { get; }

    /// <summary>Declared config shape — reported to the Cluster at registration.</summary>
    HandlerSchema Schema { get; }

    /// <summary>
    /// Execute one task. Implementations must not throw — return a failed
    /// <see cref="TaskResult"/> instead. Implementations must be idempotent
    /// because the broker may redeliver on ack failure.
    /// </summary>
    Task<TaskResult> HandleAsync(TaskContext context);
}

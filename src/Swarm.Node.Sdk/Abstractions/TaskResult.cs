namespace Swarm.Sdk.Abstractions;

/// <summary>
/// Outcome of a single handler invocation. Returned from
/// <see cref="ITaskHandler.HandleAsync(TaskContext)"/>.
/// </summary>
public record TaskResult(bool Success, string? ResultJson = null, string? ErrorMessage = null);

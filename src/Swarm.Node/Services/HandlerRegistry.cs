using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Services;

/// <summary>
/// Indexes registered <see cref="ITaskHandler"/> instances by their
/// <see cref="ITaskHandler.TaskType"/> string and resolves them at dispatch time.
/// Construction throws if two handlers claim the same task type — that is a
/// system-integrity bug we want to surface at startup, not at first message.
/// </summary>
internal sealed class HandlerRegistry
{
    private readonly Dictionary<string, ITaskHandler> _byTaskType;

    public HandlerRegistry(IEnumerable<ITaskHandler> handlers)
    {
        _byTaskType = new Dictionary<string, ITaskHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (!_byTaskType.TryAdd(handler.TaskType, handler))
            {
                var existing = _byTaskType[handler.TaskType].GetType().FullName;
                var duplicate = handler.GetType().FullName;
                throw new InvalidOperationException(
                    $"Duplicate ITaskHandler registration for TaskType '{handler.TaskType}': " +
                    $"{existing} and {duplicate}. Each TaskType@version must have exactly one handler.");
            }
        }
    }

    public IReadOnlyCollection<string> RegisteredTaskTypes => _byTaskType.Keys;

    public bool TryGet(string taskType, out ITaskHandler handler)
        => _byTaskType.TryGetValue(taskType, out handler!);
}

using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Services;

/// <summary>
/// Indexes registered <see cref="ITaskHandler"/> instances by their
/// <see cref="ITaskHandler.TaskType"/> string and resolves them at dispatch time.
/// Construction throws if two handlers claim the same task type — that is a
/// system-integrity bug we want to surface at startup, not at first message.
/// </summary>
public sealed class HandlerRegistry
{
    // ponytail: ConcurrentDictionary — reads are lock-free (hot path), writes are rare
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ITaskHandler> _byTaskType
        = new(StringComparer.Ordinal);

    public HandlerRegistry(IEnumerable<ITaskHandler> handlers)
    {
        foreach (var handler in handlers)
            TryRegister(handler, throwOnDuplicate: true);
    }

    /// <summary>Register a handler at runtime. Returns false (and logs nothing) if already registered.</summary>
    public bool TryRegister(ITaskHandler handler) => TryRegister(handler, throwOnDuplicate: false);

    private bool TryRegister(ITaskHandler handler, bool throwOnDuplicate)
    {
        if (_byTaskType.TryAdd(handler.TaskType, handler)) return true;
        if (!throwOnDuplicate) return false;
        var existing = _byTaskType[handler.TaskType].GetType().FullName;
        throw new InvalidOperationException(
            $"Duplicate ITaskHandler for '{handler.TaskType}': {existing} vs {handler.GetType().FullName}");
    }

    public IReadOnlyCollection<string> RegisteredTaskTypes => _byTaskType.Keys.ToList();

    public bool TryGet(string taskType, out ITaskHandler handler)
        => _byTaskType.TryGetValue(taskType, out handler!);
}

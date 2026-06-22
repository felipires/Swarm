using System.Reflection;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Services;

/// <summary>
/// Watches <c>Swarm:PluginsPath</c> for new *.dll files and registers any
/// <see cref="ITaskHandler"/> implementations they contain at runtime —
/// no node restart needed.
/// </summary>
internal sealed class PluginWatcherService : IHostedService, IDisposable
{
    private readonly HandlerRegistry _registry;
    private readonly string? _pluginsPath;
    private readonly ILogger<PluginWatcherService> _logger;
    private FileSystemWatcher? _watcher;

    public PluginWatcherService(HandlerRegistry registry, IConfiguration configuration, ILogger<PluginWatcherService> logger)
    {
        _registry = registry;
        _logger = logger;
        _pluginsPath = configuration["Swarm:PluginsPath"];
    }

    public Task StartAsync(CancellationToken _)
    {
        if (string.IsNullOrWhiteSpace(_pluginsPath) || !Directory.Exists(_pluginsPath))
            return Task.CompletedTask;

        _watcher = new FileSystemWatcher(_pluginsPath, "*.dll")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, e) => LoadDll(e.FullPath);
        _logger.LogInformation("Watching {Path} for new plugin DLLs", _pluginsPath);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken _) { Dispose(); return Task.CompletedTask; }

    public void Dispose() { _watcher?.Dispose(); _watcher = null; }

    private void LoadDll(string path)
    {
        try
        {
            // ponytail: Assembly.LoadFrom — same trust caveat as startup scan (P4-3)
            var asm = Assembly.LoadFrom(path);
            var registered = 0;
            foreach (var type in asm.GetTypes()
                .Where(t => typeof(ITaskHandler).IsAssignableFrom(t)
                         && !t.IsAbstract
                         && t.GetConstructor(Type.EmptyTypes) is not null))
            {
                var handler = (ITaskHandler)Activator.CreateInstance(type)!;
                if (_registry.TryRegister(handler))
                {
                    _logger.LogInformation("Hot-loaded handler {TaskType} from {Dll}", handler.TaskType, path);
                    registered++;
                }
                else
                {
                    _logger.LogWarning("Skipped duplicate handler {TaskType} from {Dll}", handler.TaskType, path);
                }
            }
            if (registered == 0)
                _logger.LogWarning("No new ITaskHandler found in {Dll}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin {Dll}", path);
        }
    }
}

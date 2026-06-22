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
    private readonly RegistrationService _registrationService;
    private readonly IServiceProvider _services;
    private readonly string? _pluginsPath;
    private readonly ILogger<PluginWatcherService> _logger;
    private FileSystemWatcher? _watcher;

    public PluginWatcherService(HandlerRegistry registry, RegistrationService registrationService, IServiceProvider services, IConfiguration configuration, ILogger<PluginWatcherService> logger)
    {
        _registry = registry;
        _registrationService = registrationService;
        _services = services;
        _logger = logger;
        _pluginsPath = configuration["Swarm:PluginsPath"];
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pluginsPath) || !Directory.Exists(_pluginsPath))
            return;

        // Scan DLLs already present before setting up the watcher.
        var registered = 0;
        foreach (var dll in Directory.GetFiles(_pluginsPath, "*.dll"))
            registered += LoadDll(dll).Count;

        if (registered > 0)
            await _registrationService.ForceRegisterWithClusterAsync();

        _watcher = new FileSystemWatcher(_pluginsPath, "*.dll")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, e) => _ = LoadDllAsync(e.FullPath);
        _logger.LogInformation("Watching {Path} for new plugin DLLs", _pluginsPath);
    }

    public Task StopAsync(CancellationToken _) { Dispose(); return Task.CompletedTask; }

    public void Dispose() { _watcher?.Dispose(); _watcher = null; }

    private async Task LoadDllAsync(string path)
    {
        var newHandlers = LoadDll(path);
        if (newHandlers.Count == 0) return;

        var executor = _services.GetService<TaskExecutorService>();
        if (executor is not null)
            foreach (var taskType in newHandlers)
                await executor.EnsureSharedQueueAsync(taskType);

        await _registrationService.ForceRegisterWithClusterAsync();
    }

    // Returns task types of newly registered handlers (empty = nothing new or error).
    private List<string> LoadDll(string path)
    {
        var registered = new List<string>();
        try
        {
            // ponytail: Assembly.LoadFrom — same trust caveat as startup scan (P4-3)
            var asm = Assembly.LoadFrom(path);
            foreach (var type in asm.GetTypes()
                .Where(t => typeof(ITaskHandler).IsAssignableFrom(t)
                         && !t.IsAbstract
                         && t.GetConstructor(Type.EmptyTypes) is not null))
            {
                var handler = (ITaskHandler)Activator.CreateInstance(type)!;
                if (_registry.TryRegister(handler))
                {
                    _logger.LogInformation("Hot-loaded handler {TaskType} from {Dll}", handler.TaskType, path);
                    registered.Add(handler.TaskType);
                }
                else
                {
                    _logger.LogWarning("Skipped duplicate handler {TaskType} from {Dll}", handler.TaskType, path);
                }
            }
            if (registered.Count == 0)
                _logger.LogWarning("No new ITaskHandler found in {Dll}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin {Dll}", path);
        }
        return registered;
    }

}

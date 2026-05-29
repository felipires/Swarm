using Microsoft.Extensions.DependencyInjection;
using Swarm.Sdk.Abstractions;

namespace Swarm.Sdk.DependencyInjection;

public static class HandlerRegistration
{
    /// <summary>
    /// Register a task handler with the DI container so the Node's task
    /// dispatcher can route by <see cref="ITaskHandler.TaskType"/>.
    /// </summary>
    public static IServiceCollection AddTaskHandler<T>(this IServiceCollection services)
        where T : class, ITaskHandler
    {
        services.AddSingleton<ITaskHandler, T>();
        return services;
    }
}

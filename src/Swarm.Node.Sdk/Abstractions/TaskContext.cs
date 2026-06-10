using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.ValueResolution;
using Swarm.Sdk.Wire;

namespace Swarm.Sdk.Abstractions;

/// <summary>
/// Execution context handed to a handler for a single dispatched task.
/// </summary>
/// <param name="Message">The raw wire message that triggered this invocation.</param>
/// <param name="Config">
/// The fully-resolved <c>TaskDefinition.ConfigJson</c>: the Node core has already
/// substituted every <c>{src:key:mods}</c> placeholder (env, param, config) and
/// re-parsed the result, so handlers can deserialize it directly — typically via
/// <see cref="GetConfig{T}"/>. Resolution failures never reach the handler; the
/// core fails the task with <c>CONFIG_RESOLUTION_FAILED</c> first.
/// </param>
/// <param name="RuntimeParams">Parsed <c>TaskMessage.RuntimeParamsJson</c>, or an undefined element if absent.</param>
/// <param name="Resolver">
/// The value-resolution pipeline (env / param / config sources). The core resolves
/// <see cref="Config"/> for you; this is exposed only for handlers that need to
/// resolve additional dynamic strings at runtime (e.g. per-item templating).
/// </param>
/// <param name="Logger">Logger pre-scoped to the handler type.</param>
/// <param name="CancellationToken">Cancellation propagated from the Node host.</param>
public record TaskContext(
    TaskMessage Message,
    JsonElement Config,
    JsonElement RuntimeParams,
    ValueResolverPipeline Resolver,
    ILogger Logger,
    CancellationToken CancellationToken)
{
    private static readonly JsonSerializerOptions DefaultJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Deserialize the already-resolved <see cref="Config"/> into the handler's
    /// strongly-typed config shape. Throws <see cref="JsonException"/> if the
    /// resolved JSON doesn't match <typeparamref name="T"/> — handlers should
    /// catch that and return a <c>CONFIG_INVALID</c> result.
    /// </summary>
    public T? GetConfig<T>(JsonSerializerOptions? options = null)
        => Config.Deserialize<T>(options ?? DefaultJsonOptions);
}

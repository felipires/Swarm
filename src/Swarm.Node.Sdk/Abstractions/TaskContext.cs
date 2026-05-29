using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.ValueResolution;
using Swarm.Sdk.Wire;

namespace Swarm.Sdk.Abstractions;

/// <summary>
/// Execution context handed to a handler for a single dispatched task.
/// </summary>
/// <param name="Message">The raw wire message that triggered this invocation.</param>
/// <param name="StaticConfig">Parsed <c>TaskDefinition.ConfigJson</c>.</param>
/// <param name="RuntimeParams">Parsed <c>TaskMessage.RuntimeParamsJson</c>, or an undefined element if absent.</param>
/// <param name="Resolver">
/// Value resolution pipeline (P1-5a). Pre-seeded with the <c>env</c>,
/// <c>param</c>, and <c>config</c> sources. Call
/// <see cref="ValueResolverPipeline.InterpolateAsync"/> on the raw
/// <c>StaticConfig.GetRawText()</c> to substitute <c>{src:key:mods}</c>
/// placeholders before parsing the resolved JSON.
/// </param>
/// <param name="Logger">Logger pre-scoped to the handler type.</param>
/// <param name="CancellationToken">Cancellation propagated from the Node host.</param>
public record TaskContext(
    TaskMessage Message,
    JsonElement StaticConfig,
    JsonElement RuntimeParams,
    ValueResolverPipeline Resolver,
    ILogger Logger,
    CancellationToken CancellationToken);

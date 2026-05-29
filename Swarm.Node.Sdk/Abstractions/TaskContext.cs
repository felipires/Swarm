using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Node.Sdk.Wire;

namespace Swarm.Node.Sdk.Abstractions;

/// <summary>
/// Execution context handed to a handler for a single dispatched task.
/// </summary>
/// <param name="Message">The raw wire message that triggered this invocation.</param>
/// <param name="StaticConfig">Parsed <c>TaskDefinition.ConfigJson</c>.</param>
/// <param name="RuntimeParams">Parsed <c>TaskMessage.RuntimeParamsJson</c>, or an undefined element if absent.</param>
/// <param name="Logger">Logger pre-scoped to the handler type.</param>
/// <param name="CancellationToken">Cancellation propagated from the Node host.</param>
/// <remarks>
/// The roadmap signature also includes a <c>ValueResolverPipeline Resolver</c>;
/// that field is deliberately omitted here and will be added when the value
/// resolution system (roadmap item P1-5a) lands. Handlers that need to read
/// resolved values today must accept that limitation.
/// </remarks>
public record TaskContext(
    TaskMessage Message,
    JsonElement StaticConfig,
    JsonElement RuntimeParams,
    ILogger Logger,
    CancellationToken CancellationToken);

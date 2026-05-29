using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.Logging;

/// <summary>
/// Per-task scope that exposes the active <see cref="ValueResolverPipeline"/>
/// to the Serilog redaction enricher (roadmap P4-2a). The enricher reads
/// <c>pipeline.Secrets</c> live so any value resolved with <c>:secret</c>
/// during a handler invocation is scrubbed from logs emitted before the
/// scope ends — even values resolved after the first log line.
///
/// Uses <see cref="AsyncLocal{T}"/> so async handlers see the same scope
/// on every continuation.
/// </summary>
public static class SecretRedactionContext
{
    private static readonly AsyncLocal<ValueResolverPipeline?> Slot = new();

    public static ValueResolverPipeline? Current => Slot.Value;

    public static IDisposable Push(ValueResolverPipeline pipeline)
    {
        var prev = Slot.Value;
        Slot.Value = pipeline;
        return new Scope(prev);
    }

    private sealed class Scope : IDisposable
    {
        private readonly ValueResolverPipeline? _previous;
        public Scope(ValueResolverPipeline? previous) => _previous = previous;
        public void Dispose() => Slot.Value = _previous;
    }
}

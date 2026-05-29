using Serilog.Core;
using Serilog.Events;

namespace Swarm.Node.Logging;

/// <summary>
/// Serilog enricher that replaces any scalar string property value
/// containing a resolved <c>:secret</c> with <c>[REDACTED]</c> (roadmap
/// P4-2a). Reads the active <see cref="SecretRedactionContext.Current"/>
/// pipeline at enrich time, so secrets resolved later during the same
/// handler invocation are still redacted in earlier-emitted events that
/// have not yet been sunk.
///
/// Limitation: only structured properties are scrubbed. A handler that
/// emits secrets via a non-parameterized message template
/// (<c>$"Token={secret}"</c>) bakes the secret into the template itself
/// and the enricher cannot rewrite it. Use parameterized logging.
/// </summary>
public class SecretRedactionEnricher : ILogEventEnricher
{
    public const string RedactedToken = "[REDACTED]";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var pipeline = SecretRedactionContext.Current;
        if (pipeline is null) return;
        var secrets = pipeline.Secrets;
        if (secrets.Count == 0) return;

        foreach (var (key, value) in logEvent.Properties.ToList())
        {
            var replacement = TryRedact(value, secrets);
            if (replacement is not null)
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, replacement));
        }
    }

    /// <summary>
    /// Returns a redacted replacement for <paramref name="value"/> if it
    /// contains any secret; otherwise <c>null</c> (caller leaves untouched).
    /// Walks one level into structured + sequence values so nested fields
    /// like <c>{ headers = { Authorization = "Bearer ..." } }</c> are
    /// covered.
    /// </summary>
    private static LogEventPropertyValue? TryRedact(LogEventPropertyValue value, IReadOnlyCollection<string> secrets)
    {
        switch (value)
        {
            case ScalarValue { Value: string s }:
                var redacted = RedactString(s, secrets);
                return redacted is null ? null : new ScalarValue(redacted);

            case StructureValue sv:
                List<LogEventProperty>? newProps = null;
                for (int i = 0; i < sv.Properties.Count; i++)
                {
                    var rep = TryRedact(sv.Properties[i].Value, secrets);
                    if (rep is null) continue;
                    newProps ??= sv.Properties.ToList();
                    newProps[i] = new LogEventProperty(sv.Properties[i].Name, rep);
                }
                return newProps is null ? null : new StructureValue(newProps, sv.TypeTag);

            case SequenceValue seq:
                List<LogEventPropertyValue>? newItems = null;
                for (int i = 0; i < seq.Elements.Count; i++)
                {
                    var rep = TryRedact(seq.Elements[i], secrets);
                    if (rep is null) continue;
                    newItems ??= seq.Elements.ToList();
                    newItems[i] = rep;
                }
                return newItems is null ? null : new SequenceValue(newItems);

            default:
                return null;
        }
    }

    private static string? RedactString(string s, IReadOnlyCollection<string> secrets)
    {
        string? working = null;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrEmpty(secret)) continue;
            if ((working ?? s).Contains(secret, StringComparison.Ordinal))
                working = (working ?? s).Replace(secret, RedactedToken);
        }
        return working;
    }
}

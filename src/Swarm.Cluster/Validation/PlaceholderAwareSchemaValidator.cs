using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Cluster.Validation;

/// <summary>
/// Validates a handler config blob against the handler's JSON Schema, lenient
/// about <c>{src:key:mods}</c> placeholders (roadmap D4 / P1-7a Cluster side).
/// Because placeholders may appear at JSON VALUE positions (where they're
/// strictly invalid JSON) we do a syntax-aware substitution pass first to
/// produce a parseable doc, then validate that doc against the schema.
///
/// Substitution rules:
///   • Placeholder inside a JSON string (between unescaped quotes) → replaced
///     with a stable alphanumeric token in-place, keeping the string valid.
///   • Placeholder at a JSON value position → replaced with a literal of the
///     <c>type=</c> modifier (default <c>"string"</c>). A <c>:default=X</c>
///     modifier is honored when present.
///
/// The Node still re-validates post-resolution (P1-7a Node side) which catches
/// any cases the lenient Cluster check accepted.
/// </summary>
public static class PlaceholderAwareSchemaValidator
{
    public const string Sentinel = PlaceholderSubstitution.Sentinel;

    public sealed record ValidationFailure(string Path, string Message);

    /// <summary>
    /// Validate <paramref name="configJson"/> against the handler's
    /// <paramref name="schemaJson"/>. Returns empty list on success.
    /// </summary>
    public static IReadOnlyList<ValidationFailure> Validate(string configJson, string schemaJson)
    {
        var prepared = SubstitutePlaceholders(configJson);

        JsonNode? configNode;
        try
        {
            configNode = JsonNode.Parse(prepared);
        }
        catch (JsonException ex)
        {
            return new[] { new ValidationFailure("$", $"INVALID_CONFIG_JSON: {ex.Message}") };
        }

        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson);
        }
        catch (Exception ex)
        {
            // A broken schema is a Node-side bug — surface as a distinct
            // signal (caller can map to CAPABILITY_DIVERGENCE if appropriate).
            return new[] { new ValidationFailure("$", $"INVALID_HANDLER_SCHEMA: {ex.Message}") };
        }

        var result = schema.Evaluate(configNode, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (result.IsValid) return Array.Empty<ValidationFailure>();

        var failures = new List<ValidationFailure>();
        Collect(result, failures);

        // Drop failures at paths whose value is the placeholder sentinel: those
        // fields are supplied at runtime (notably value-position {…:type=json},
        // whose resolved type — object/array/scalar — the Cluster cannot know).
        // Their real shape is checked by the handler at execution time.
        failures = failures
            .Where(f => !ValueAtPathIsSentinel(configNode, f.Path))
            .ToList();

        return failures;
    }

    /// <summary>
    /// Resolves a JSON Pointer (as produced by the schema evaluator's instance
    /// location, e.g. <c>/headers</c>) against the substituted config and
    /// returns true when the value there is exactly the placeholder sentinel.
    /// </summary>
    private static bool ValueAtPathIsSentinel(JsonNode? root, string pointer)
    {
        if (root is null || string.IsNullOrEmpty(pointer) || pointer == "$") return false;

        var node = root;
        foreach (var rawSeg in pointer.Split('/'))
        {
            if (rawSeg.Length == 0) continue; // leading empty segment before first '/'
            var seg = rawSeg.Replace("~1", "/").Replace("~0", "~");
            switch (node)
            {
                case JsonObject obj when obj.TryGetPropertyValue(seg, out var child):
                    node = child;
                    break;
                case JsonArray arr when int.TryParse(seg, out var idx) && idx >= 0 && idx < arr.Count:
                    node = arr[idx];
                    break;
                default:
                    return false;
            }
        }

        return node is JsonValue v && v.TryGetValue<string>(out var s) && s == Sentinel;
    }

    private static void Collect(EvaluationResults node, List<ValidationFailure> failures)
    {
        if (node.HasErrors && node.Errors is not null)
        {
            foreach (var (_, msg) in node.Errors)
                failures.Add(new ValidationFailure(node.InstanceLocation.ToString(), msg));
        }

        if (node.Details is not null)
        {
            foreach (var child in node.Details)
                Collect(child, failures);
        }
    }

    /// <summary>
    /// Rewrites value/string-position placeholders into surrogate literals so
    /// the template parses as JSON. Delegates to the shared
    /// <see cref="PlaceholderSubstitution"/> in the SDK (the Node config-staging
    /// path uses the exact same logic). Visible for testing.
    /// </summary>
    internal static string SubstitutePlaceholders(string source)
        => PlaceholderSubstitution.ToParseableJson(source);
}

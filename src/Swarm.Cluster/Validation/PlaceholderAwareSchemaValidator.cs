using System.Text;
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
    public const string Sentinel = "__SWARM_PLACEHOLDER__";

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
        return failures;
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
    /// Walks <paramref name="source"/> tracking JSON string state and replaces
    /// every <c>{src:key:mods}</c> with a syntactically valid surrogate so the
    /// result parses as JSON. Visible for testing.
    /// </summary>
    internal static string SubstitutePlaceholders(string source)
    {
        var placeholders = PlaceholderParser.Extract(source);
        if (placeholders.Count == 0) return source;

        // For each placeholder figure out if it's inside a string literal by
        // scanning the source up to its Start and counting unescaped quotes.
        var sb = new StringBuilder(source.Length);
        var cursor = 0;
        foreach (var p in placeholders)
        {
            sb.Append(source, cursor, p.Start - cursor);
            sb.Append(InsideString(source, p.Start) ? StringSurrogate(p) : ValueSurrogate(p));
            cursor = p.Start + p.Length;
        }
        sb.Append(source, cursor, source.Length - cursor);
        return sb.ToString();
    }

    /// <summary>
    /// Returns true if <paramref name="position"/> is inside an unterminated
    /// JSON string literal. Counts unescaped quotes from start of input.
    /// </summary>
    private static bool InsideString(string source, int position)
    {
        bool inString = false;
        for (int i = 0; i < position; i++)
        {
            var c = source[i];
            if (c == '\\' && i + 1 < position)
            {
                i++;             // skip the escaped char
                continue;
            }
            if (c == '"') inString = !inString;
        }
        return inString;
    }

    private static string StringSurrogate(Placeholder p)
        => GetDefault(p) ?? Sentinel;

    private static string ValueSurrogate(Placeholder p)
    {
        var defaultMod = GetDefault(p);
        var type = p.Modifiers
            .FirstOrDefault(m => m.StartsWith("type=", StringComparison.Ordinal))
            ?.Substring("type=".Length);

        return type switch
        {
            "int" => defaultMod ?? "0",
            "float" => defaultMod ?? "0.0",
            "bool" => defaultMod ?? "false",
            "json" => defaultMod ?? "null",
            _ => defaultMod is null ? $"\"{Sentinel}\"" : $"\"{defaultMod}\"",
        };
    }

    private static string? GetDefault(Placeholder p)
    {
        var mod = p.Modifiers.FirstOrDefault(m => m.StartsWith("default=", StringComparison.Ordinal));
        return mod?.Substring("default=".Length);
    }
}

using System.Text;

namespace Swarm.Sdk.ValueResolution;

/// <summary>
/// Rewrites a config template that may contain value-position
/// <c>{src:key:mods}</c> placeholders into syntactically valid JSON by
/// replacing each placeholder with a typed surrogate. Used wherever a template
/// must be <em>parsed</em> before its real values are known:
///   • Cluster schema pre-validation (<c>PlaceholderAwareSchemaValidator</c>).
///   • Node config staging — so <c>{config:}</c> cross-references and
///     <c>TaskContext.StaticConfig</c> have a parseable document even when the
///     template carries unquoted value-position placeholders.
///
/// Substitution rules:
///   • Placeholder inside a JSON string (between unescaped quotes) → replaced
///     with a stable token in-place, keeping the string valid.
///   • Placeholder at a JSON value position → replaced with a literal of the
///     <c>type=</c> modifier (default <c>"string"</c>). A <c>:default=X</c>
///     modifier is honored when present.
///
/// This is intentionally lenient: the real values are substituted later by
/// <see cref="ValueResolverPipeline"/>. The surrogates exist only so the
/// document parses.
/// </summary>
public static class PlaceholderSubstitution
{
    public const string Sentinel = "__SWARM_PLACEHOLDER__";

    /// <summary>
    /// Walks <paramref name="source"/> tracking JSON string state and replaces
    /// every <c>{src:key:mods}</c> with a syntactically valid surrogate so the
    /// result parses as JSON.
    /// </summary>
    public static string ToParseableJson(string source)
    {
        if (string.IsNullOrEmpty(source)) return source;

        var placeholders = PlaceholderParser.Extract(source);
        if (placeholders.Count == 0) return source;

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
            // type=json is runtime-typed (object/array/scalar/null) — there is no
            // surrogate literal that satisfies an arbitrary schema. Emit the
            // sentinel string so schema validation can exempt this path; a
            // concrete default, when given, is used verbatim.
            "json" => defaultMod ?? $"\"{Sentinel}\"",
            _ => defaultMod is null ? $"\"{Sentinel}\"" : $"\"{defaultMod}\"",
        };
    }

    private static string? GetDefault(Placeholder p)
    {
        var mod = p.Modifiers.FirstOrDefault(m => m.StartsWith("default=", StringComparison.Ordinal));
        return mod?.Substring("default=".Length);
    }
}

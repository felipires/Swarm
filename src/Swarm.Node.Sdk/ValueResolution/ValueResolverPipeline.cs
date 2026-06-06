using System.Globalization;
using System.Text;

namespace Swarm.Sdk.ValueResolution;

/// <summary>
/// Composes <see cref="IValueResolver"/>s by source name and walks a template
/// to substitute placeholders. Modifier semantics (<c>required</c>,
/// <c>default=...</c>, <c>secret</c>, <c>type=...</c>) live here so resolvers
/// stay pure source readers.
/// </summary>
public class ValueResolverPipeline
{
    private readonly Dictionary<string, IValueResolver> _resolvers;
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);

    public ValueResolverPipeline(IEnumerable<IValueResolver> resolvers)
    {
        _resolvers = resolvers.ToDictionary(r => r.Source, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolved secret values seen since this pipeline was constructed. The
    /// redaction enricher (P4-2a) reads this set to scrub log output.
    /// </summary>
    public IReadOnlyCollection<string> Secrets => _secrets;

    /// <summary>
    /// Walk the template and replace every placeholder. Throws
    /// <see cref="ValueResolutionException"/> on missing-required, unknown
    /// source, or coercion failure.
    /// </summary>
    public async Task<string> InterpolateAsync(string template, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

        var placeholders = PlaceholderParser.Extract(template);
        if (placeholders.Count == 0) return template;

        var sb = new StringBuilder(template.Length);
        int cursor = 0;
        foreach (var p in placeholders)
        {
            sb.Append(template, cursor, p.Start - cursor);
            sb.Append(await ResolveOneAsync(p, template, cancellationToken));
            cursor = p.Start + p.Length;
        }
        sb.Append(template, cursor, template.Length - cursor);

        return sb.ToString();
    }

    /// <summary>
    /// Like <see cref="InterpolateAsync"/> but every <c>secret</c> placeholder
    /// is replaced with <c>[REDACTED]</c>. Use for log rendering.
    /// </summary>
    public async Task<string> InterpolateSafeAsync(string template, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

        var placeholders = PlaceholderParser.Extract(template);
        if (placeholders.Count == 0) return template;

        var sb = new StringBuilder(template.Length);
        int cursor = 0;
        foreach (var p in placeholders)
        {
            sb.Append(template, cursor, p.Start - cursor);
            if (p.Modifiers.Contains("secret"))
                sb.Append("[REDACTED]");
            else
                sb.Append(await ResolveOneAsync(p, template, cancellationToken));
            cursor = p.Start + p.Length;
        }
        sb.Append(template, cursor, template.Length - cursor);
        return sb.ToString();
    }

    private async Task<string> ResolveOneAsync(Placeholder p, string template, CancellationToken ct)
    {
        if (!_resolvers.TryGetValue(p.Source, out var resolver))
            throw new ValueResolutionException(
                $"Unknown placeholder source '{p.Source}' in '{template.Substring(p.Start, p.Length)}'");

        var resolved = await resolver.ResolveAsync(p.Key, ct);
        string raw;
        bool isSecret = p.Modifiers.Contains("secret");

        if (resolved is null)
        {
            if (p.Modifiers.Contains("required"))
                throw new ValueResolutionException(
                    $"Required value missing: {p.Source}:{p.Key}");

            var defaultMod = p.Modifiers.FirstOrDefault(m => m.StartsWith("default=", StringComparison.Ordinal));
            if (defaultMod is null)
                return string.Empty;

            raw = defaultMod.Substring("default=".Length);
        }
        else
        {
            raw = resolved.Raw;
            if (resolved.IsSecret) isSecret = true;
        }

        if (isSecret && !string.IsNullOrEmpty(raw))
            _secrets.Add(raw);

        return CoerceToType(p, raw);
    }

    private static string JsonEscapeString(string raw)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(raw);
        return json[1..^1]; // strip surrounding quotes
    }

    private static string CoerceToType(Placeholder p, string raw)
    {
        var typeMod = p.Modifiers.FirstOrDefault(m => m.StartsWith("type=", StringComparison.Ordinal));
        if (typeMod is null) return JsonEscapeString(raw);

        var t = typeMod.Substring("type=".Length);
        return t switch
        {
            "string" => raw,
            "int" => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i.ToString(CultureInfo.InvariantCulture)
                : throw new ValueResolutionException($"Cannot coerce '{raw}' to int for {p.Source}:{p.Key}"),
            "float" => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f.ToString("R", CultureInfo.InvariantCulture)
                : throw new ValueResolutionException($"Cannot coerce '{raw}' to float for {p.Source}:{p.Key}"),
            "bool" => raw.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true"
                : raw.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false"
                : throw new ValueResolutionException($"Cannot coerce '{raw}' to bool for {p.Source}:{p.Key}"),
            "json" => raw,   // injected as-is at value position
            _ => throw new ValueResolutionException($"Unknown type modifier '{t}' for {p.Source}:{p.Key}"),
        };
    }
}

public class ValueResolutionException : Exception
{
    public ValueResolutionException(string message) : base(message) { }
}

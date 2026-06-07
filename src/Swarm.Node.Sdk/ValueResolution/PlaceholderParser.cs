using System.Text.RegularExpressions;

namespace Swarm.Sdk.ValueResolution;

/// <summary>
/// Parses <c>{source:key[:modifier1:modifier2...]}</c> placeholders out of a
/// template. The regex is strict — a literal <c>{</c> followed by anything
/// that isn't a valid <c>source:key</c> body (e.g. a JSON object opening
/// brace) is left untouched. No <c>{{</c>/<c>}}</c> escape mechanism is
/// needed because the strict shape already disambiguates placeholders from
/// JSON braces.
/// </summary>
public static partial class PlaceholderParser
{
    // Source: lowercase letter, then [a-z0-9_].
    // Key:    one or more chars from [A-Za-z0-9_.-] (supports dot-notation,
    //         common env-var conventions).
    // Modifiers: any number of `:segment`, segment is non-empty chars
    //         excluding `{`, `}`, `:`. Captures `default=...`, `type=...`,
    //         `required`, `secret` etc. verbatim.
    [GeneratedRegex(@"\{(?<source>[a-z][a-z0-9_]*)\s*:\s*(?<key>[A-Za-z0-9_.\-]+)(?<mods>(?::[^\{\}:]+)*)\}", RegexOptions.Compiled)]
    internal static partial Regex PlaceholderRegex();

    /// <summary>
    /// Expand <c>{param:KEY}</c> references in <paramref name="template"/>
    /// using <paramref name="lookup"/>. Non-param sources and unknown keys are
    /// left as-is. Used for one-level self-resolution of runtime params.
    /// </summary>
    public static string ExpandParamRefs(string template, IReadOnlyDictionary<string, string> lookup)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("{param:", StringComparison.Ordinal))
            return template;

        return PlaceholderRegex().Replace(template, m =>
        {
            if (m.Groups["source"].Value != "param") return m.Value;
            var key = m.Groups["key"].Value;
            return lookup.TryGetValue(key, out var val) ? val : m.Value;
        });
    }

    public static IReadOnlyList<Placeholder> Extract(string template)
    {
        var result = new List<Placeholder>();
        if (string.IsNullOrEmpty(template)) return result;

        foreach (Match m in PlaceholderRegex().Matches(template))
        {
            var modText = m.Groups["mods"].Value;
            var modifiers = modText.Length == 0
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : modText.Split(':', StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => s.Trim())
                         .Where(s => s.Length > 0)
                         .ToList();

            result.Add(new Placeholder(
                Start: m.Index,
                Length: m.Length,
                Source: m.Groups["source"].Value,
                Key: m.Groups["key"].Value,
                Modifiers: modifiers));
        }

        return result;
    }
}

using System.Text.Json;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.ValueResolution;

/// <summary>
/// Resolves <c>config:KEY</c> placeholders against the parsed
/// <c>TaskDefinition.ConfigJson</c>. Supports dot notation for nested access.
/// Enables cross-field references inside a single config blob.
/// </summary>
public class ConfigResolver : IValueResolver
{
    private readonly JsonElement _root;

    public ConfigResolver(JsonElement root)
    {
        _root = root;
    }

    public string Source => "config";

    public Task<ResolvedValue?> ResolveAsync(string key, CancellationToken cancellationToken)
    {
        if (_root.ValueKind != JsonValueKind.Object)
            return Task.FromResult<ResolvedValue?>(null);

        var current = _root;
        foreach (var segment in key.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return Task.FromResult<ResolvedValue?>(null);
            if (!current.TryGetProperty(segment, out current)) return Task.FromResult<ResolvedValue?>(null);
        }

        return Task.FromResult<ResolvedValue?>(new ResolvedValue(JsonElementToString(current)));
    }

    private static string JsonElementToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText(),
    };
}

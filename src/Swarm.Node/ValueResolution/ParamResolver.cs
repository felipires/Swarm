using System.Text.Json;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Node.ValueResolution;

/// <summary>
/// Resolves <c>param:KEY</c> placeholders against the per-dispatch
/// <see cref="JsonElement"/> of runtime params. Supports dot notation
/// (<c>address.city</c>) for nested object access.
/// </summary>
public class ParamResolver : IValueResolver
{
    private readonly JsonElement _root;

    public ParamResolver(JsonElement root)
    {
        _root = root;
    }

    public string Source => "param";

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

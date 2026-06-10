using System.Text.Json;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// A single log row as returned by the search endpoint. <see cref="Tags"/> is
/// the parsed jsonb correlation/context map (task / run / step / pipeline /
/// env.*) so the UI can render clickable tag chips.
/// </summary>
public record LogResponse(
    Guid Id,
    Guid? NodeId,
    string Level,
    string MessageTemplate,
    string? Message,
    string? Exception,
    Dictionary<string, string>? Tags,
    DateTime Timestamp)
{
    public static LogResponse From(Log log) => new(
        log.Id,
        log.NodeId,
        log.Level,
        log.MessageTemplate,
        log.Message,
        log.Exception,
        ParseTags(log.Tags),
        log.Timestamp);

    private static Dictionary<string, string>? ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException) { return null; }
    }
}

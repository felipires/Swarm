using System.Text.Json;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Models.Dto;

/// <summary>
/// A version-history entry (P1-10). The list endpoints return metadata only
/// (<see cref="Snapshot"/> null); the single-version endpoint includes the
/// snapshot as raw JSON so the UI can render the definition at that version.
/// </summary>
public class EntityVersionResponse
{
    public int Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public JsonElement? Snapshot { get; init; }

    public static EntityVersionResponse Meta(EntityVersion v) => new()
    {
        Version = v.Version,
        CreatedAt = v.CreatedAt,
    };

    public static EntityVersionResponse Full(EntityVersion v) => new()
    {
        Version = v.Version,
        CreatedAt = v.CreatedAt,
        Snapshot = JsonSerializer.Deserialize<JsonElement>(v.SnapshotJson),
    };
}

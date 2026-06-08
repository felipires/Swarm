using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services;

/// <summary>
/// Append-only version history for tasks and pipelines (P1-10). A history row is
/// written on every create / update / restore; the snapshot is the
/// create-request-shaped definition at that version, so a restore re-applies it
/// through the normal write path and the UI renders it with existing components.
///
/// Scoped — shares the request's <see cref="ClusterDbContext"/> so the history
/// write commits in the same transaction as the entity write. It only stages the
/// row (Add); the caller owns SaveChanges.
/// </summary>
public class EntityVersionService
{
    private readonly ClusterDbContext _db;

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public EntityVersionService(ClusterDbContext db) => _db = db;

    /// <summary>Stage a history row for the given version. Does not SaveChanges.</summary>
    public void Record<TSnapshot>(VersionedEntityType type, Guid entityId, int version, TSnapshot snapshot)
    {
        _db.EntityVersions.Add(new EntityVersion
        {
            Id = Guid.NewGuid(),
            EntityType = type,
            EntityId = entityId,
            Version = version,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }

    /// <summary>Version metadata for an entity, newest first.</summary>
    public async Task<List<EntityVersion>> ListAsync(
        VersionedEntityType type, Guid entityId, CancellationToken cancellationToken)
        => await _db.EntityVersions
            .Where(v => v.EntityType == type && v.EntityId == entityId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

    /// <summary>A single version's row (with its snapshot), or null.</summary>
    public async Task<EntityVersion?> GetAsync(
        VersionedEntityType type, Guid entityId, int version, CancellationToken cancellationToken)
        => await _db.EntityVersions
            .FirstOrDefaultAsync(
                v => v.EntityType == type && v.EntityId == entityId && v.Version == version,
                cancellationToken);

    /// <summary>Deserialize a snapshot back into its create-request shape.</summary>
    public static TSnapshot Deserialize<TSnapshot>(string snapshotJson)
        => JsonSerializer.Deserialize<TSnapshot>(snapshotJson, SnapshotJsonOptions)!;
}

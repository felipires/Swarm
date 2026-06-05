using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;

namespace Swarm.Cluster.Services.Tags;

/// <summary>
/// GIN-indexed Postgres tag matcher (P3-3). Uses the jsonb <c>@&gt;</c>
/// containment operator against <c>Nodes."EffectiveTags"</c> (and the inverse
/// against <c>TaggedRoutes."SelectorJson"</c>), which the GIN index on
/// <c>EffectiveTags</c> accelerates. All user-derived values are passed as
/// parameters via interpolation — nothing is concatenated into the SQL text.
///
/// The selector is canonicalized through <see cref="EffectiveTags.Serialize"/>
/// so its byte form lines up with the stored projection.
/// </summary>
public sealed class PostgresJsonbTagMatcher : ITagMatchStrategy
{
    private readonly ClusterDbContext _db;

    public PostgresJsonbTagMatcher(ClusterDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> MatchEligibleNodesAsync(
        IReadOnlyDictionary<string, string> selector,
        string taskType,
        CancellationToken cancellationToken)
    {
        // Empty selector can't be expressed as a meaningful containment; callers
        // (DispatchTaggedAsync) already reject it, but guard anyway.
        var selectorJson = EffectiveTags.Serialize(selector);
        if (selectorJson is null) return Array.Empty<Guid>();

        // Online Nodes advertising the TaskType whose effective tags contain the
        // selector. DISTINCT because a Node may advertise the TaskType once but
        // the join shape is defensive.
        var ids = await _db.Database
            .SqlQuery<Guid>($"""
                SELECT DISTINCT n."Id" AS "Value"
                FROM "Nodes" n
                JOIN "NodeCapabilities" c ON c."NodeId" = n."Id"
                WHERE n."Status" = 0
                  AND c."TaskType" = {taskType}
                  AND n."EffectiveTagsJson" @> {selectorJson}::jsonb
                """)
            .ToListAsync(cancellationToken);
        return ids;
    }

    public async Task<IReadOnlyList<string>> MatchRoutesForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        // A route applies to the Node iff the Node's effective tags contain the
        // route selector: EffectiveTags @> SelectorJson.
        var hashes = await _db.Database
            .SqlQuery<string>($"""
                SELECT r."Hash" AS "Value"
                FROM "TaggedRoutes" r
                JOIN "Nodes" n ON n."Id" = {nodeId}
                WHERE n."EffectiveTagsJson" IS NOT NULL
                  AND n."EffectiveTagsJson" @> r."SelectorJson"
                """)
            .ToListAsync(cancellationToken);
        return hashes;
    }
}

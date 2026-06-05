namespace Swarm.Cluster.Services.Tags;

/// <summary>
/// Tag-containment matching for <c>TaggedNodes</c> routing (P3-3). The DB is a
/// boundary (roadmap DIP rule), so the two containment queries live behind this
/// interface: production uses a GIN-indexed Postgres <c>@&gt;</c> query
/// (<see cref="PostgresJsonbTagMatcher"/>); tests and non-Postgres providers use
/// an in-memory LINQ matcher (<see cref="InMemoryTagMatcher"/>) that is also the
/// single semantic reference both implementations honor.
///
/// "Effective tags" means a Node's denormalized <c>static ∪ overlay</c>
/// projection (<see cref="Models.Node.EffectiveTagsJson"/>); see
/// <see cref="EffectiveTags"/>.
/// </summary>
public interface ITagMatchStrategy
{
    /// <summary>
    /// IDs of online Nodes whose effective tags are a superset of
    /// <paramref name="selector"/> AND that advertise <paramref name="taskType"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> MatchEligibleNodesAsync(
        IReadOnlyDictionary<string, string> selector,
        string taskType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hashes of <see cref="Models.TaggedRoute"/>s whose selector is a subset of
    /// the given Node's effective tags — i.e. the tagged queues that Node should
    /// consume.
    /// </summary>
    Task<IReadOnlyList<string>> MatchRoutesForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken);
}

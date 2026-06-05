using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Tags;

/// <summary>
/// LINQ tag-superset matcher (P3-3). The test / non-Postgres path, and the
/// single definition of the <em>semantics</em> the Postgres matcher must match.
/// Reads <see cref="Node.EffectiveTagsJson"/> when present, falling back to
/// recomputing <c>static ∪ overlay</c> on the fly so that callers seeding only
/// <see cref="Node.StaticTagsJson"/> (e.g. older tests) still match correctly.
/// </summary>
public sealed class InMemoryTagMatcher : ITagMatchStrategy
{
    private readonly ClusterDbContext _db;

    public InMemoryTagMatcher(ClusterDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> MatchEligibleNodesAsync(
        IReadOnlyDictionary<string, string> selector,
        string taskType,
        CancellationToken cancellationToken)
    {
        // Online Nodes advertising the TaskType. Filter the tag superset in
        // memory after materializing — EF can't translate the dictionary
        // containment, and this is the deliberately-untranslated reference path.
        var candidates = await _db.Nodes
            .Where(n => n.Status == Node.NodeStatus.Online)
            .Join(_db.NodeCapabilities.Where(c => c.TaskType == taskType),
                  n => n.Id, c => c.NodeId, (n, _) => n)
            .Distinct()
            .ToListAsync(cancellationToken);

        var overlayByNode = await LoadOverlayAsync(
            candidates.Select(n => n.Id), cancellationToken);

        var eligible = new List<Guid>();
        foreach (var node in candidates)
        {
            var effective = ResolveEffective(node, overlayByNode);
            if (IsSuperset(effective, selector))
                eligible.Add(node.Id);
        }
        return eligible;
    }

    public async Task<IReadOnlyList<string>> MatchRoutesForNodeAsync(
        Guid nodeId,
        CancellationToken cancellationToken)
    {
        var node = await _db.Nodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);
        if (node is null) return Array.Empty<string>();

        var overlayByNode = await LoadOverlayAsync(new[] { nodeId }, cancellationToken);
        var effective = ResolveEffective(node, overlayByNode);
        if (effective.Count == 0) return Array.Empty<string>();

        var routes = await _db.TaggedRoutes.ToListAsync(cancellationToken);
        var matched = new List<string>();
        foreach (var route in routes)
        {
            var selector = EffectiveTags.Parse(route.SelectorJson);
            if (selector.Count == 0) continue;
            if (IsSuperset(effective, selector))
                matched.Add(route.Hash);
        }
        return matched;
    }

    private async Task<ILookup<Guid, NodeOverlayTag>> LoadOverlayAsync(
        IEnumerable<Guid> nodeIds, CancellationToken ct)
    {
        var ids = nodeIds.ToList();
        var rows = await _db.NodeOverlayTags
            .Where(t => ids.Contains(t.NodeId))
            .ToListAsync(ct);
        return rows.ToLookup(t => t.NodeId);
    }

    private static Dictionary<string, string> ResolveEffective(
        Node node, ILookup<Guid, NodeOverlayTag> overlayByNode)
        => node.EffectiveTagsJson is { Length: > 0 } eff
            ? EffectiveTags.Parse(eff)
            : EffectiveTags.Compose(node.StaticTagsJson, overlayByNode[node.Id]);

    private static bool IsSuperset(
        IReadOnlyDictionary<string, string> effective,
        IReadOnlyDictionary<string, string> selector)
        => selector.All(req => effective.TryGetValue(req.Key, out var v) && v == req.Value);
}

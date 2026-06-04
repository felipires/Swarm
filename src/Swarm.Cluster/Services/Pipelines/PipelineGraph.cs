using Swarm.Cluster.Models;

namespace Swarm.Cluster.Services.Pipelines;

/// <summary>
/// Pure DAG logic for pipelines (roadmap P1-1). Validates structure
/// (no cycles, no dangling references, no duplicate names) at
/// authoring time; resolves "which steps are now ready to dispatch"
/// at runtime given the current per-step state map.
///
/// No EF / DI dependencies — easy to unit-test exhaustively.
/// </summary>
public sealed class PipelineGraph
{
    private readonly Dictionary<Guid, GraphNode> _byId;
    private readonly Dictionary<string, Guid> _idByName;
    private readonly IReadOnlyList<Guid> _topo;

    private PipelineGraph(Dictionary<Guid, GraphNode> byId, IReadOnlyList<Guid> topologicalOrder)
    {
        _byId = byId;
        _idByName = byId.Values.ToDictionary(n => n.Name, n => n.Id, StringComparer.OrdinalIgnoreCase);
        _topo = topologicalOrder;
    }

    public IReadOnlyCollection<Guid> StepIds => _byId.Keys;
    public IReadOnlyList<Guid> TopologicalOrder => _topo;
    public int Count => _byId.Count;

    public GraphNode this[Guid id] => _byId[id];
    public bool TryResolveByName(string name, out Guid id) => _idByName.TryGetValue(name, out id);

    /// <summary>
    /// Build a graph from a set of <see cref="PipelineStep"/>s, validating:
    ///   • no duplicate names (case-insensitive)
    ///   • every <c>DependsOnJson</c> reference points at a step in this set
    ///   • the dependency relation is acyclic
    /// Throws <see cref="PipelineGraphException"/> on any violation.
    /// </summary>
    public static PipelineGraph Build(IReadOnlyCollection<PipelineStep> steps)
    {
        if (steps.Count == 0)
            throw new PipelineGraphException("EMPTY_PIPELINE", "Pipeline must have at least one step");

        var byId = new Dictionary<Guid, GraphNode>(steps.Count);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            if (!seenNames.Add(step.Name))
                throw new PipelineGraphException("DUPLICATE_STEP_NAME",
                    $"Duplicate step name '{step.Name}' in pipeline");

            var deps = DependencyDecoder.Decode(step.DependsOnJson);
            byId[step.Id] = new GraphNode(step.Id, step.Name, deps);
        }

        // Validate dependency references and detect self-loops.
        foreach (var node in byId.Values)
        {
            foreach (var dep in node.DependsOn)
            {
                if (dep == node.Id)
                    throw new PipelineGraphException("SELF_LOOP",
                        $"Step '{node.Name}' depends on itself");
                if (!byId.ContainsKey(dep))
                    throw new PipelineGraphException("DANGLING_DEPENDENCY",
                        $"Step '{node.Name}' depends on unknown step {dep}");
            }
        }

        var topo = TopologicalSort(byId);
        return new PipelineGraph(byId, topo);
    }

    /// <summary>Step IDs with zero dependencies. Dispatched first when the run starts.</summary>
    public IReadOnlyList<Guid> RootIds()
        => _byId.Values.Where(n => n.DependsOn.Count == 0).Select(n => n.Id).ToList();

    /// <summary>
    /// Steps whose dependencies are all <see cref="PipelineStepInstanceStatus.Completed"/>
    /// in the supplied state map and that are themselves still
    /// <see cref="PipelineStepInstanceStatus.Waiting"/>. The caller is
    /// responsible for transitioning them to <c>Dispatched</c> atomically
    /// with the actual dispatch.
    /// </summary>
    public IReadOnlyList<Guid> ResolveNextReady(IReadOnlyDictionary<Guid, PipelineStepInstanceStatus> stateByStepId)
    {
        var ready = new List<Guid>();
        foreach (var node in _byId.Values)
        {
            if (!stateByStepId.TryGetValue(node.Id, out var status) || status != PipelineStepInstanceStatus.Waiting)
                continue;

            var allDepsCompleted = node.DependsOn.All(d =>
                stateByStepId.TryGetValue(d, out var depStatus)
                && depStatus == PipelineStepInstanceStatus.Completed);
            if (allDepsCompleted) ready.Add(node.Id);
        }
        return ready;
    }

    /// <summary>
    /// Transitive descendants of <paramref name="stepId"/> — every step whose
    /// dependency chain leads back to it. Used when a step fails with
    /// <see cref="StepFailurePolicy.FailPipeline"/> to compute which
    /// Waiting steps need to flip to Skipped.
    /// </summary>
    public IReadOnlySet<Guid> Descendants(Guid stepId)
    {
        if (!_byId.ContainsKey(stepId)) return new HashSet<Guid>();

        // BFS over the reverse-adjacency built lazily here. For typical
        // pipeline sizes (< 100 steps) building this on demand is fine.
        var revAdj = new Dictionary<Guid, List<Guid>>(_byId.Count);
        foreach (var node in _byId.Values)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!revAdj.TryGetValue(dep, out var list))
                    revAdj[dep] = list = new List<Guid>();
                list.Add(node.Id);
            }
        }

        var result = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(stepId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!revAdj.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                if (result.Add(child)) queue.Enqueue(child);
            }
        }
        return result;
    }

    private static IReadOnlyList<Guid> TopologicalSort(Dictionary<Guid, GraphNode> byId)
    {
        // Kahn's algorithm. Stable order: nodes are dequeued in insertion order.
        var inDegree = byId.ToDictionary(kv => kv.Key, kv => kv.Value.DependsOn.Count);
        var queue = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<Guid>(byId.Count);

        // Reverse adjacency: for each node, which nodes depend on it?
        var dependents = new Dictionary<Guid, List<Guid>>(byId.Count);
        foreach (var node in byId.Values)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!dependents.TryGetValue(dep, out var list))
                    dependents[dep] = list = new List<Guid>();
                list.Add(node.Id);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);
            if (!dependents.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                inDegree[child]--;
                if (inDegree[child] == 0) queue.Enqueue(child);
            }
        }

        if (order.Count != byId.Count)
        {
            var unsorted = inDegree.Where(kv => kv.Value > 0).Select(kv => byId[kv.Key].Name);
            throw new PipelineGraphException("CYCLE_DETECTED",
                $"Pipeline contains a cycle involving step(s): {string.Join(", ", unsorted)}");
        }

        return order;
    }

    public sealed record GraphNode(Guid Id, string Name, IReadOnlyList<Guid> DependsOn);

    /// <summary>
    /// Parses <c>PipelineStep.DependsOnJson</c> into a Guid list. Tolerates
    /// null/empty as "no dependencies".
    /// </summary>
    public static class DependencyDecoder
    {
        public static IReadOnlyList<Guid> Decode(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]")
                return Array.Empty<Guid>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PipelineGraphException("INVALID_DEPENDS_ON_JSON", ex.Message);
            }
        }

        public static string Encode(IEnumerable<Guid> dependsOn)
            => System.Text.Json.JsonSerializer.Serialize(dependsOn.ToArray());
    }
}

public class PipelineGraphException : Exception
{
    public string Code { get; }
    public PipelineGraphException(string code, string message) : base(message) => Code = code;
}

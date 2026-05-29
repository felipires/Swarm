using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swarm.Cluster.Data;
using Swarm.Cluster.Models;
using Swarm.Sdk.ValueResolution;

namespace Swarm.Cluster.Validation;

/// <summary>
/// Pre-flight checks run by <see cref="Services.TaskDispatchService"/>
/// before persisting a TaskInstance + PendingDispatch (roadmap P1-7).
/// Cheap, synchronous-ish checks that fail fast with a typed exception so
/// the controller can return a 400 instead of letting the message rot in
/// the broker.
///
/// Phase-1 scope (smaller than the roadmap's full vision):
///   • TaskType has at least one eligible online Node (UNSUPPORTED_TASK_TYPE).
///   • ConfigJson and RuntimeParamsJson are syntactically valid (INVALID_*_JSON).
///   • Every <c>{param:K:required}</c> placeholder has a matching key in
///     the supplied runtime params (MISSING_REQUIRED_PARAMS).
///   • Every key in the handler's <c>RequiredParams</c> is supplied
///     (MISSING_REQUIRED_PARAMS).
///   • Every key in the handler's <c>RequiredEnvKeys</c> is referenced by
///     the config placeholders (MISSING_REQUIRED_ENV_DECLARATION). Actual
///     env presence on Nodes is deferred until the env-management API
///     (P1-5a Cluster side) lands.
///
/// Deferred to follow-ups: full JSON Schema validation (would add a JSON
/// Schema dependency), CAPABILITY_DIVERGENCE detection across Nodes.
/// </summary>
public class DispatchValidator
{
    private readonly ClusterDbContext _db;

    public DispatchValidator(ClusterDbContext db)
    {
        _db = db;
    }

    public async Task ValidateAsync(
        TaskDefinition definition,
        string? runtimeParamsJson,
        DispatchStrategy strategy,
        Guid? targetNodeId,
        CancellationToken cancellationToken)
    {
        // 1. Config JSON must be parseable.
        JsonElement configDoc;
        try
        {
            configDoc = JsonDocument.Parse(definition.ConfigJson).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new DispatchValidationException("INVALID_CONFIG_JSON", ex.Message);
        }

        // 2. RuntimeParamsJson if supplied.
        JsonElement paramsDoc = default;
        if (!string.IsNullOrEmpty(runtimeParamsJson))
        {
            try
            {
                paramsDoc = JsonDocument.Parse(runtimeParamsJson).RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new DispatchValidationException("INVALID_PARAMS_JSON", ex.Message);
            }
        }

        // 3. Eligibility: at least one online Node must advertise the TaskType.
        //    For SpecificNode the target NodeId itself must advertise it.
        var capabilitiesQuery = _db.NodeCapabilities
            .Join(_db.Nodes, c => c.NodeId, n => n.Id, (c, n) => new { Capability = c, Node = n })
            .Where(x => x.Capability.TaskType == definition.TaskType && x.Node.Status == Node.NodeStatus.Online);

        if (strategy == DispatchStrategy.SpecificNode && targetNodeId is not null)
            capabilitiesQuery = capabilitiesQuery.Where(x => x.Node.Id == targetNodeId);

        var eligibleCaps = await capabilitiesQuery.Select(x => x.Capability).ToListAsync(cancellationToken);
        if (eligibleCaps.Count == 0)
            throw new DispatchValidationException(
                "UNSUPPORTED_TASK_TYPE",
                $"No online Node advertises TaskType '{definition.TaskType}'",
                new { definition.TaskType });

        // 4. Placeholder presence checks.
        var placeholders = PlaceholderParser.Extract(definition.ConfigJson);
        var missingRequiredParams = placeholders
            .Where(p => p.Source == "param" && p.Modifiers.Contains("required"))
            .Select(p => p.Key)
            .Distinct()
            .Where(k => !ParamExists(paramsDoc, k))
            .ToList();
        if (missingRequiredParams.Count > 0)
            throw new DispatchValidationException(
                "MISSING_REQUIRED_PARAMS",
                $"Missing required param keys: {string.Join(", ", missingRequiredParams)}",
                new { Missing = missingRequiredParams });

        // 5. Handler-declared required params (intersection across all eligible caps).
        var declaredRequired = IntersectRequiredParams(eligibleCaps);
        var missingDeclared = declaredRequired.Where(k => !ParamExists(paramsDoc, k)).ToList();
        if (missingDeclared.Count > 0)
            throw new DispatchValidationException(
                "MISSING_REQUIRED_PARAMS",
                $"Handler declares required params: {string.Join(", ", missingDeclared)}",
                new { Missing = missingDeclared });

        // 6. Handler-declared required env: must at least appear as
        //    {env:K} placeholders. Actual presence on Nodes is checked later
        //    once the env-management API lands.
        var declaredEnv = IntersectRequiredEnv(eligibleCaps);
        var envPlaceholderKeys = placeholders
            .Where(p => p.Source == "env")
            .Select(p => p.Key)
            .ToHashSet(StringComparer.Ordinal);
        var undeclared = declaredEnv.Where(k => !envPlaceholderKeys.Contains(k)).ToList();
        if (undeclared.Count > 0)
            throw new DispatchValidationException(
                "MISSING_REQUIRED_ENV_DECLARATION",
                $"Handler declares env keys not referenced by config: {string.Join(", ", undeclared)}",
                new { Missing = undeclared });
    }

    private static bool ParamExists(JsonElement root, string dottedKey)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        var current = root;
        foreach (var segment in dottedKey.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return false;
            if (!current.TryGetProperty(segment, out current)) return false;
        }
        return true;
    }

    private static HashSet<string> IntersectRequiredParams(IReadOnlyList<NodeCapability> caps)
    {
        if (caps.Count == 0) return new HashSet<string>();
        var first = JsonSerializer.Deserialize<List<string>>(caps[0].RequiredParamsJson) ?? new();
        var result = new HashSet<string>(first, StringComparer.Ordinal);
        for (int i = 1; i < caps.Count; i++)
        {
            var next = JsonSerializer.Deserialize<List<string>>(caps[i].RequiredParamsJson) ?? new();
            result.IntersectWith(next);
        }
        return result;
    }

    private static HashSet<string> IntersectRequiredEnv(IReadOnlyList<NodeCapability> caps)
    {
        if (caps.Count == 0) return new HashSet<string>();
        // Env keys: union, not intersection — if any eligible Node requires
        // an env key and the config doesn't reference it, the resolution
        // will fail at that Node. Safer to flag the union.
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cap in caps)
        {
            var list = JsonSerializer.Deserialize<List<string>>(cap.RequiredEnvKeysJson) ?? new();
            foreach (var k in list) result.Add(k);
        }
        return result;
    }
}

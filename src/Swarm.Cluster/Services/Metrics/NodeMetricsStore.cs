using System.Text.Json;
using StackExchange.Redis;

namespace Swarm.Cluster.Services.Metrics;

/// <summary>
/// Stores and retrieves per-node live metrics in Redis. Storage is TTL-based
/// (self-expiring) — no background GC service needed. A dead node's metrics
/// disappear automatically after <c>Metrics:TtlSeconds</c> (default 300s).
///
/// Two keys per node:
///   node:metrics:latest:{nodeId}  — latest snapshot (SET EX ttl)
///   node:metrics:history:{nodeId} — capped list of recent samples (LPUSH + LTRIM + EXPIRE)
/// </summary>
public class NodeMetricsStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NodeMetricsStore> _logger;
    private readonly int _ttlSeconds;
    private readonly int _historyLength;

    private static string LatestKey(Guid nodeId) => $"node:metrics:latest:{nodeId:N}";
    private static string HistoryKey(Guid nodeId) => $"node:metrics:history:{nodeId:N}";

    public NodeMetricsStore(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<NodeMetricsStore> logger)
    {
        _redis = redis;
        _logger = logger;
        _ttlSeconds = configuration.GetValue<int>("Metrics:TtlSeconds", 3600);
        _historyLength = configuration.GetValue<int>("Metrics:HistoryLength", 120);
    }

    /// <summary>
    /// Store a metrics snapshot. Best-effort — logs and returns on Redis failure.
    /// </summary>
    public async Task StoreAsync(Guid nodeId, NodeMetricsSnapshot snapshot)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(snapshot);
            var ttl = TimeSpan.FromSeconds(_ttlSeconds);

            await db.StringSetAsync(LatestKey(nodeId), json, ttl);

            await db.ListLeftPushAsync(HistoryKey(nodeId), json);
            await db.ListTrimAsync(HistoryKey(nodeId), 0, _historyLength - 1);
            await db.KeyExpireAsync(HistoryKey(nodeId), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis unavailable — skipping metrics store for node {NodeId}", nodeId);
        }
    }

    /// <summary>
    /// Retrieve the latest snapshot for a node. Returns null if not cached.
    /// </summary>
    public async Task<NodeMetricsSnapshot?> GetLatestAsync(Guid nodeId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var raw = await db.StringGetAsync(LatestKey(nodeId));
            if (raw.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<NodeMetricsSnapshot>(raw!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis unavailable — skipping latest metrics for node {NodeId}", nodeId);
            return null;
        }
    }

    /// <summary>
    /// Retrieve the recent history for a node (newest-first). Returns empty list
    /// if not cached or Redis is unreachable.
    /// </summary>
    public async Task<List<NodeMetricsSnapshot>> GetHistoryAsync(Guid nodeId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var entries = await db.ListRangeAsync(HistoryKey(nodeId), 0, _historyLength - 1);
            var result = new List<NodeMetricsSnapshot>(entries.Length);
            foreach (var entry in entries)
            {
                if (entry.IsNullOrEmpty) continue;
                var snap = JsonSerializer.Deserialize<NodeMetricsSnapshot>(entry!);
                if (snap is not null) result.Add(snap);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis unavailable — skipping metrics history for node {NodeId}", nodeId);
            return new List<NodeMetricsSnapshot>();
        }
    }
}

public record NodeMetricsSnapshot
{
    public DateTime RecordedAt { get; init; }
    public double CpuPercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long MemoryAvailableBytes { get; init; }
    public int InFlightTasks { get; init; }
    public long UptimeSeconds { get; init; }
    public string Health { get; init; } = "Healthy";
}

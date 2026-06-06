using System.Text.Json;

namespace Swarm.Cluster.Models.Dto;

public class NodeResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Node.NodeStatus Status { get; set; } = Node.NodeStatus.Offline;
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // P5-1: static hardware capacity — null until the Node sends a NodeCapacity message.
    public int? CpuCores { get; set; }
    public long? TotalMemoryBytes { get; set; }

    // P3-3: effective tag set (static ∪ overlay, static wins).
    public Dictionary<string, string> EffectiveTags { get; set; } = new();

    // P0-3b: TaskType@version identifiers this Node advertises.
    public List<string> Capabilities { get; set; } = new();

    // P5-1: latest live metrics from Redis; null if not yet received or Redis unavailable.
    public NodeMetricsDto? LatestMetrics { get; set; }

    public static NodeResponse From(
        Node node,
        IReadOnlyList<string>? capabilities = null,
        NodeMetricsDto? latestMetrics = null) => new()
    {
        Id = node.Id,
        Name = node.Name,
        Status = node.Status,
        LastHeartbeatAt = node.LastHeartbeatAt,
        CreatedAt = node.CreatedAt,
        CpuCores = node.CpuCores,
        TotalMemoryBytes = node.TotalMemoryBytes,
        EffectiveTags = ParseTags(node.EffectiveTagsJson),
        Capabilities = capabilities?.ToList() ?? new(),
        LatestMetrics = latestMetrics,
    };

    private static Dictionary<string, string> ParseTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

public class NodeMetricsDto
{
    public DateTime RecordedAt { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public long MemoryAvailableBytes { get; set; }
    public int InFlightTasks { get; set; }
    public long UptimeSeconds { get; set; }
    public string Health { get; set; } = "Healthy";
}

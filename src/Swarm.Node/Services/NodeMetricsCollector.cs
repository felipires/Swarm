using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Swarm.Node.Services;

/// <summary>
/// Reads process- and system-level resource counters for P5-1 heartbeat metrics.
/// Designed to be called once per heartbeat — no background polling loop of its own.
/// </summary>
public class NodeMetricsCollector
{
    private readonly ILogger<NodeMetricsCollector> _logger;
    private readonly DateTime _processStartTime;
    private readonly Process _process;

    // Thresholds for the derived health status.
    private const double DegradedCpuThreshold = 85.0;
    private const double UnhealthyCpuThreshold = 95.0;
    private const double DegradedMemThreshold = 0.90;
    private const double UnhealthyMemThreshold = 0.97;

    // CPU tracking — we need two samples to compute a delta.
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastCpuSample = DateTime.MinValue;

    public NodeMetricsCollector(ILogger<NodeMetricsCollector> logger)
    {
        _logger = logger;
        _process = Process.GetCurrentProcess();
        _processStartTime = _process.StartTime.ToUniversalTime();
    }

    /// <summary>
    /// Reports the static hardware capacity that is invariant for the
    /// lifetime of this process. Called once at registration.
    /// </summary>
    public static NodeHardwareCapacity ReadCapacity()
    {
        return new NodeHardwareCapacity
        {
            CpuCores = Environment.ProcessorCount,
            TotalMemoryBytes = ReadTotalMemoryBytes(),
        };
    }

    /// <summary>
    /// Samples current live gauges. Returns a snapshot with a derived
    /// <see cref="NodeHealthStatus"/> computed from configurable thresholds.
    /// </summary>
    public NodeLiveMetrics ReadMetrics(int inFlightTasks)
    {
        var cpuPercent = SampleCpuPercent();
        var (memUsed, memAvailable) = SampleMemory();
        var uptime = (long)(DateTime.UtcNow - _processStartTime).TotalSeconds;
        var health = DeriveHealth(cpuPercent, memUsed, memAvailable);

        return new NodeLiveMetrics
        {
            CpuPercent = cpuPercent,
            MemoryUsedBytes = memUsed,
            MemoryAvailableBytes = memAvailable,
            InFlightTasks = inFlightTasks,
            UptimeSeconds = uptime,
            Health = health,
        };
    }

    private double SampleCpuPercent()
    {
        try
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var totalCpu = _process.TotalProcessorTime;

            if (_lastCpuSample == DateTime.MinValue)
            {
                _lastCpuTime = totalCpu;
                _lastCpuSample = now;
                return 0.0;
            }

            var elapsed = (now - _lastCpuSample).TotalMilliseconds;
            if (elapsed < 100)
                return 0.0;

            var cpuDelta = (totalCpu - _lastCpuTime).TotalMilliseconds;
            _lastCpuTime = totalCpu;
            _lastCpuSample = now;

            var cores = Math.Max(1, Environment.ProcessorCount);
            return Math.Round(cpuDelta / (elapsed * cores) * 100.0, 1);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CPU sampling failed");
            return 0.0;
        }
    }

    private (long used, long available) SampleMemory()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            var total = info.TotalAvailableMemoryBytes;
            _process.Refresh();
            var used = _process.WorkingSet64;
            var available = Math.Max(0, total - used);
            return (used, available);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory sampling failed");
            return (0, 0);
        }
    }

    private static long ReadTotalMemoryBytes()
    {
        // Prefer cgroup-aware value from GC (correct in Linux containers).
        var gcInfo = GC.GetGCMemoryInfo();
        if (gcInfo.TotalAvailableMemoryBytes > 0)
            return gcInfo.TotalAvailableMemoryBytes;

        // Fallback: WMI on Windows or /proc/meminfo on Linux are not portable
        // enough to bother with here; return 0 to signal "unknown".
        return 0;
    }

    internal static NodeHealthStatus DeriveHealth(double cpuPercent, long memUsed, long memAvailable)
    {
        var total = memUsed + memAvailable;
        var memRatio = total > 0 ? (double)memUsed / total : 0.0;

        if (cpuPercent >= UnhealthyCpuThreshold || memRatio >= UnhealthyMemThreshold)
            return NodeHealthStatus.Unhealthy;
        if (cpuPercent >= DegradedCpuThreshold || memRatio >= DegradedMemThreshold)
            return NodeHealthStatus.Degraded;
        return NodeHealthStatus.Healthy;
    }
}

public record NodeHardwareCapacity
{
    public int CpuCores { get; init; }
    public long TotalMemoryBytes { get; init; }
}

public record NodeLiveMetrics
{
    public double CpuPercent { get; init; }
    public long MemoryUsedBytes { get; init; }
    public long MemoryAvailableBytes { get; init; }
    public int InFlightTasks { get; init; }
    public long UptimeSeconds { get; init; }
    public NodeHealthStatus Health { get; init; }
}

public enum NodeHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
}

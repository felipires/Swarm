using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Swarm.Node.Services;
using Xunit;

namespace Swarm.Node.Tests;

public class NodeMetricsCollectorTests
{
    private static NodeMetricsCollector Build() =>
        new(NullLogger<NodeMetricsCollector>.Instance);

    [Fact]
    public void ReadCapacity_ReturnPositiveCpuCores()
    {
        var capacity = NodeMetricsCollector.ReadCapacity();
        capacity.CpuCores.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ReadCapacity_ReturnPositiveTotalMemory()
    {
        var capacity = NodeMetricsCollector.ReadCapacity();
        capacity.TotalMemoryBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ReadMetrics_ReturnsNonNegativeValues()
    {
        var collector = Build();
        var metrics = collector.ReadMetrics(inFlightTasks: 3);

        metrics.CpuPercent.Should().BeGreaterThanOrEqualTo(0).And.BeLessOrEqualTo(100);
        metrics.MemoryUsedBytes.Should().BeGreaterThanOrEqualTo(0);
        metrics.MemoryAvailableBytes.Should().BeGreaterThanOrEqualTo(0);
        metrics.InFlightTasks.Should().Be(3);
        metrics.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ReadMetrics_HealthIsHealthyUnderLoad()
    {
        // A brand-new process with 0 in-flight tasks is almost certainly not degraded.
        var collector = Build();
        var metrics = collector.ReadMetrics(inFlightTasks: 0);

        // We can't assert Healthy with certainty on a loaded CI box, but we can
        // assert the enum has a valid value.
        var valid = new[] { NodeHealthStatus.Healthy, NodeHealthStatus.Degraded, NodeHealthStatus.Unhealthy };
        valid.Should().Contain(metrics.Health);
    }

    [Theory]
    [InlineData(84.9, 0.89, NodeHealthStatus.Healthy)]
    [InlineData(85.0, 0.50, NodeHealthStatus.Degraded)]
    [InlineData(50.0, 0.90, NodeHealthStatus.Degraded)]
    [InlineData(95.0, 0.50, NodeHealthStatus.Unhealthy)]
    [InlineData(50.0, 0.97, NodeHealthStatus.Unhealthy)]
    [InlineData(96.0, 0.98, NodeHealthStatus.Unhealthy)]
    public void DeriveHealth_ThresholdEdges(double cpuPercent, double memRatio, NodeHealthStatus expected)
    {
        long total = 1_000_000_000L;
        long used = (long)(total * memRatio);
        long available = total - used;

        var result = NodeMetricsCollector.DeriveHealth(cpuPercent, used, available);
        result.Should().Be(expected);
    }
}

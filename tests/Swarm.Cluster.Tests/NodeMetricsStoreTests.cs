using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Swarm.Cluster.Services.Metrics;
using Xunit;

namespace Swarm.Cluster.Tests;

/// <summary>
/// Integration-tier tests for <see cref="NodeMetricsStore"/>. Runs only when
/// <c>SWARM_TEST_REDIS_CONN</c> is set to a reachable Redis instance; otherwise
/// every fact no-ops so the unit loop stays green without infrastructure.
/// </summary>
[Trait("Category", "Integration")]
public class NodeMetricsStoreTests : IAsyncLifetime
{
    private static string? ConnString => Environment.GetEnvironmentVariable("SWARM_TEST_REDIS_CONN");

    private IConnectionMultiplexer? _mux;
    private NodeMetricsStore? _store;
    private readonly Guid _nodeId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnString)) return;

        _mux = await ConnectionMultiplexer.ConnectAsync(ConnString);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Metrics:TtlSeconds"] = "60",
                ["Metrics:HistoryLength"] = "10",
            })
            .Build();
        _store = new NodeMetricsStore(_mux, config, NullLogger<NodeMetricsStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null)
        {
            // Clean up test keys.
            var db = _mux.GetDatabase();
            await db.KeyDeleteAsync($"node:metrics:latest:{_nodeId:N}");
            await db.KeyDeleteAsync($"node:metrics:history:{_nodeId:N}");
            await _mux.DisposeAsync();
        }
    }

    [Fact]
    public async Task StoreAndGetLatest_RoundTrips()
    {
        if (_store is null) return;

        var snap = MakeSnapshot("Healthy");
        await _store.StoreAsync(_nodeId, snap);

        var latest = await _store.GetLatestAsync(_nodeId);

        latest.Should().NotBeNull();
        latest!.CpuPercent.Should().Be(snap.CpuPercent);
        latest.Health.Should().Be("Healthy");
        latest.InFlightTasks.Should().Be(snap.InFlightTasks);
    }

    [Fact]
    public async Task StoreMultiple_HistoryIsNewestFirst()
    {
        if (_store is null) return;

        for (var i = 0; i < 3; i++)
        {
            var snap = MakeSnapshot("Healthy", cpuPercent: i * 10.0);
            await _store.StoreAsync(_nodeId, snap);
        }

        var history = await _store.GetHistoryAsync(_nodeId);

        history.Should().HaveCountGreaterThanOrEqualTo(3);
        // LPUSH puts newest at index 0; CPU 20 should appear before CPU 0.
        history[0].CpuPercent.Should().Be(20.0);
    }

    [Fact]
    public async Task GetLatest_MissingNode_ReturnsNull()
    {
        if (_store is null) return;

        var result = await _store.GetLatestAsync(Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetHistory_MissingNode_ReturnsEmptyList()
    {
        if (_store is null) return;

        var result = await _store.GetHistoryAsync(Guid.NewGuid());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Store_WithoutRedis_DoesNotThrow()
    {
        // Verify graceful degradation: a store backed by an unreachable Redis
        // must log-and-continue, never throw.
        var brokenMux = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "localhost:16379" },
            AbortOnConnectFail = false,
            ConnectTimeout = 200,
            ReconnectRetryPolicy = new NoRetry(),
        });
        var config = new ConfigurationBuilder().Build();
        var brokenStore = new NodeMetricsStore(brokenMux, config, NullLogger<NodeMetricsStore>.Instance);

        var act = async () => await brokenStore.StoreAsync(Guid.NewGuid(), MakeSnapshot("Healthy"));
        await act.Should().NotThrowAsync();
    }

    private static NodeMetricsSnapshot MakeSnapshot(string health, double cpuPercent = 42.0) =>
        new()
        {
            RecordedAt = DateTime.UtcNow,
            CpuPercent = cpuPercent,
            MemoryUsedBytes = 512_000_000,
            MemoryAvailableBytes = 512_000_000,
            InFlightTasks = 2,
            UptimeSeconds = 1234,
            Health = health,
        };
}

/// <summary>Never retry — used to fail fast against a port that has nothing listening.</summary>
file sealed class NoRetry : IReconnectRetryPolicy
{
    public bool ShouldRetry(long currentRetryCount, int timeElapsedMillisecondsSinceLastRetry) => false;
}

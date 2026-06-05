using Npgsql;

namespace Swarm.Cluster.Services;

/// <summary>
/// Periodically deletes <c>TaggedRoutes</c> rows whose <c>LastUsedAt</c> is
/// older than <c>TaggedRoutes:RetentionDays</c> (default 30). Without this,
/// every distinct tag selector ever dispatched leaves a permanent row, and the
/// tag matcher reads the table on every heartbeat to compute each Node's
/// tagged-queue subscriptions — so stale rows inflate a hot-path query for the
/// life of the deployment.
///
/// <para>
/// <c>LastUsedAt</c> alone is a safe GC predicate: deleting a route just stops
/// Nodes being told to subscribe to its <c>tasks.tagged.&lt;hash&gt;</c> queue
/// (the Node-side subscription diff cancels the dropped queue), and any future
/// dispatch to the same selector deterministically recreates the row. With a
/// 30-day default a route can only be pruned long after any task could still be
/// sitting unclaimed on its queue.
/// </para>
///
/// Uses the shared <see cref="NpgsqlDataSource"/> singleton directly — no EF
/// Core, so it can safely live for the application lifetime (P0-1 pattern).
/// Mirrors <see cref="LogRetentionService"/>.
/// </summary>
public class TaggedRouteRetentionService : BackgroundService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<TaggedRouteRetentionService> _logger;
    private readonly int _retentionDays;
    private readonly TimeSpan _interval;

    public TaggedRouteRetentionService(NpgsqlDataSource db, IConfiguration configuration, ILogger<TaggedRouteRetentionService> logger)
    {
        _db = db;
        _logger = logger;
        _retentionDays = configuration.GetValue<int>("TaggedRoutes:RetentionDays", 30);
        _interval = TimeSpan.FromHours(configuration.GetValue<int>("TaggedRoutes:RetentionCheckIntervalHours", 6));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_retentionDays <= 0)
        {
            _logger.LogInformation(
                "Tagged-route retention disabled (TaggedRoutes:RetentionDays <= 0); service is a no-op");
            return;
        }

        _logger.LogInformation(
            "Tagged-route retention service started — keeping routes used within {RetentionDays} days, sweeping every {Interval}",
            _retentionDays, _interval);

        // Run once at startup so a long-stopped Cluster doesn't have to wait for
        // the first interval tick to trim its backlog.
        await TrimOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await TrimOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tagged-route retention sweep failed; will retry on the next tick");
            }
        }

        _logger.LogInformation("Tagged-route retention service stopping");
    }

    internal async Task<int> TrimOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"TaggedRoutes\" WHERE \"LastUsedAt\" < @cutoff";
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation("Tagged-route retention deleted {Count} route(s) unused since {Cutoff:O}", deleted, cutoff);
        }

        return deleted;
    }
}

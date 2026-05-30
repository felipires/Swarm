using Npgsql;

namespace Swarm.Cluster.Services;

/// <summary>
/// Periodically deletes <c>Logs</c> rows older than
/// <c>Logging:RetentionDays</c> (default 30). Without this, the logs table
/// grows unbounded and eventually starves the Cluster's Postgres instance.
/// Uses the shared <see cref="NpgsqlDataSource"/> singleton directly — no
/// EF Core, so it can safely live for the application lifetime (roadmap P3-4
/// / P0-1 pattern).
/// </summary>
public class LogRetentionService : BackgroundService
{
    private readonly NpgsqlDataSource _db;
    private readonly ILogger<LogRetentionService> _logger;
    private readonly int _retentionDays;
    private readonly TimeSpan _interval;

    public LogRetentionService(NpgsqlDataSource db, IConfiguration configuration, ILogger<LogRetentionService> logger)
    {
        _db = db;
        _logger = logger;
        _retentionDays = configuration.GetValue<int>("Logging:RetentionDays", 30);
        _interval = TimeSpan.FromHours(configuration.GetValue<int>("Logging:RetentionCheckIntervalHours", 6));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_retentionDays <= 0)
        {
            _logger.LogInformation(
                "Log retention disabled (Logging:RetentionDays <= 0); service is a no-op");
            return;
        }

        _logger.LogInformation(
            "Log retention service started — keeping {RetentionDays} days of logs, sweeping every {Interval}",
            _retentionDays, _interval);

        // Run once at startup so a long-stopped Cluster doesn't have to wait
        // for the first interval tick to trim its backlog.
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
                _logger.LogError(ex, "Log retention sweep failed; will retry on the next tick");
            }
        }

        _logger.LogInformation("Log retention service stopping");
    }

    internal async Task<int> TrimOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

        await using var conn = await _db.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM \"Logs\" WHERE \"Timestamp\" < @cutoff";
        cmd.Parameters.AddWithValue("cutoff", cutoff);

        var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation("Log retention deleted {Count} rows older than {Cutoff:O}", deleted, cutoff);
        }

        return deleted;
    }
}

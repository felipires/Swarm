using System;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Swarm.Node.Data;

public class AppDbConnection(IOptions<DataConfiguration> config, ILogger<AppDbConnection> logger)
{
    private readonly DataConfiguration _config = config.Value;
    private readonly ILogger<AppDbConnection> _logger = logger;

    public string GetConnectionString()
    {
        return _config.ConnectionString;
    }

    public Task SetupDatabaseAsync()
    {
        _logger.LogDebug("Setting up database with connection string: {ConnectionString}", _config.ConnectionString);
        _logger.LogDebug("Running database migrations from Data/Migrations folder");
       
        var migrationFiles = Directory.GetFiles("Data/Migrations", "*.sql").OrderBy(f => f).ToList();
        _logger.LogDebug("Found {MigrationCount} migration files", migrationFiles.Count);

        using var connection = new SqliteConnection(_config.ConnectionString);
        connection.Open();

        foreach (var file in migrationFiles)
        {
            _logger.LogDebug("Running migration file: {FileName}", file);
            var sql = File.ReadAllText(file);
            _logger.LogDebug("Migration SQL: {Sql}", sql);

            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }
}

using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Handlers;

/// <summary>
/// Built-in <c>sql@1</c> handler — executes a parameterized query against
/// Postgres, SQL Server, or MySQL/MariaDB. Provider is selected via the
/// <c>provider</c> field on the config (defaults to <c>postgres</c>). All
/// three providers accept <c>@param</c>-style parameter markers in the
/// query, so the dispatch layer normalizes nothing — write the query in
/// the dialect the provider expects.
/// </summary>
public sealed class SqlHandlerV1 : TaskHandler<SqlHandlerV1.SqlHandlerConfig>
{
    public override string TaskType => "sql@1";

    public override HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["connectionString", "query"],
              "properties": {
                "provider": { "type": "string", "enum": ["postgres", "postgresql", "mssql", "sqlserver", "mysql", "mariadb"] },
                "connectionString": { "type": "string" },
                "query": { "type": "string" },
                "parameters": { "type": "object" },
                "rowLimit": { "type": "integer", "minimum": 1 }
              }
            }
            """,
    };

    protected override async Task<TaskResult> HandleAsync(SqlHandlerConfig config, TaskContext context)
    {
        if (string.IsNullOrEmpty(config.ConnectionString) || string.IsNullOrEmpty(config.Query))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: connectionString and query are required");

        DbConnection conn;
        try
        {
            conn = OpenConnection(config.Provider, config.ConnectionString);
        }
        catch (InvalidOperationException ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_RESOLUTION_INVALID: {ex.Message}");
        }

        var rowLimit = config.RowLimit > 0 ? config.RowLimit : 10000;

        try
        {
            await using (conn)
            {
                await conn.OpenAsync(context.CancellationToken);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = config.Query;

                if (config.Parameters is { Count: > 0 })
                {
                    foreach (var (k, v) in config.Parameters)
                    {
                        var param = cmd.CreateParameter();
                        param.ParameterName = k;
                        param.Value = (object?)v ?? DBNull.Value;
                        cmd.Parameters.Add(param);
                    }
                }

                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await cmd.ExecuteReaderAsync(context.CancellationToken);
                while (await reader.ReadAsync(context.CancellationToken) && rows.Count < rowLimit)
                {
                    var row = new Dictionary<string, object?>(reader.FieldCount);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    rows.Add(row);
                }

                var resultJson = JsonSerializer.Serialize(new { rowCount = rows.Count, rows });
                return new TaskResult(true, ResultJson: resultJson);
            }
        }
        catch (DbException ex)
        {
            return new TaskResult(false, ErrorMessage: $"SQL_EXECUTION_FAILED: {ex.Message}");
        }
    }

    /// <summary>
    /// Construct a typed <see cref="DbConnection"/> for the requested provider.
    /// Three are supported in Phase 1; add more by extending the switch and
    /// the schema <c>enum</c>.
    /// </summary>
    internal static DbConnection OpenConnection(string? provider, string connectionString)
        => (provider?.ToLowerInvariant() ?? "postgres") switch
        {
            "" or "postgres" or "postgresql" => new NpgsqlConnection(connectionString),
            "mssql" or "sqlserver" => new SqlConnection(connectionString),
            "mysql" or "mariadb" => new MySqlConnection(connectionString),
            var p => throw new InvalidOperationException($"Unsupported SQL provider '{p}'"),
        };

    public sealed class SqlHandlerConfig
    {
        /// <summary>postgres | mssql | mysql; defaults to postgres.</summary>
        public string? Provider { get; set; }
        public string? ConnectionString { get; set; }
        public string? Query { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public int RowLimit { get; set; }
    }
}

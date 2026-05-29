using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Handlers;

/// <summary>
/// Built-in <c>sql@1</c> handler — executes a parameterized Postgres query
/// and returns rows as a JSON array. Phase 1 supports Postgres only; the
/// roadmap leaves multi-flavor SQL for a follow-up.
/// </summary>
public sealed class SqlHandlerV1 : ITaskHandler
{
    public string TaskType => "sql@1";

    public HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["connectionString", "query"],
              "properties": {
                "connectionString": { "type": "string" },
                "query": { "type": "string" },
                "parameters": { "type": "object" },
                "rowLimit": { "type": "integer", "minimum": 1 }
              }
            }
            """,
    };

    public async Task<TaskResult> HandleAsync(TaskContext context)
    {
        string resolved;
        try
        {
            resolved = await context.Resolver.InterpolateAsync(
                context.StaticConfig.GetRawText(), context.CancellationToken);
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_RESOLUTION_FAILED: {ex.Message}");
        }

        SqlHandlerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<SqlHandlerConfig>(resolved, JsonOpts);
        }
        catch (JsonException ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_RESOLUTION_INVALID: {ex.Message}");
        }

        if (config is null || string.IsNullOrEmpty(config.ConnectionString) || string.IsNullOrEmpty(config.Query))
            return new TaskResult(false, ErrorMessage: "CONFIG_RESOLUTION_INVALID: connectionString and query are required");

        var rowLimit = config.RowLimit > 0 ? config.RowLimit : 10000;

        try
        {
            await using var conn = new NpgsqlConnection(config.ConnectionString);
            await conn.OpenAsync(context.CancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = config.Query;

            if (config.Parameters is { Count: > 0 })
            {
                foreach (var (k, v) in config.Parameters)
                    cmd.Parameters.Add(new NpgsqlParameter(k, (object?)v ?? DBNull.Value));
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
        catch (NpgsqlException ex)
        {
            return new TaskResult(false, ErrorMessage: $"SQL_EXECUTION_FAILED: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class SqlHandlerConfig
    {
        public string? ConnectionString { get; set; }
        public string? Query { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public int RowLimit { get; set; }
    }
}

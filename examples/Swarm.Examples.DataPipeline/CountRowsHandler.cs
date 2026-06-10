using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.Abstractions;

namespace Swarm.Examples.DataPipeline;

/// <summary>
/// Counts lines in a file and returns { "rowCount": N }.
/// Designed as step-1 or step-2 in a pipeline where a downstream step
/// reads rowCount via an output mapping.
///
/// Config shape:
/// {
///   "filePath": "{param:filePath:required}"
/// }
///
/// Result JSON — consumed by downstream steps via output mapping:
/// { "rowCount": 1500 }
///
/// Example pipeline definition (two parallel count steps feeding step-3):
///
///   step-1 (count-rows@1)  ─┐
///                            ├─► step-3 (report@1)
///   step-2 (count-rows@1)  ─┘
///
///   step-3 output mappings:
///     rowCount → n1  from step-1
///     rowCount → n2  from step-2
///
///   step-3 params:
///     { "query": "SELECT {param:n1} + {param:n2};" }
/// </summary>
public sealed class CountRowsHandler : TaskHandler<CountRowsHandler.CountRowsConfig>
{
    public override string TaskType => "count-rows@1";

    public override HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["filePath"],
              "properties": {
                "filePath": { "type": "string" }
              }
            }
            """,
        RequiredParams = ["filePath"],
    };

    protected override async Task<TaskResult> HandleAsync(CountRowsConfig config, TaskContext context)
    {
        if (string.IsNullOrWhiteSpace(config.FilePath))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: filePath is required");

        if (!File.Exists(config.FilePath))
            return new TaskResult(false, ErrorMessage: $"FILE_NOT_FOUND: {config.FilePath}");

        try
        {
            var rowCount = 0;
            await foreach (var _ in ReadLinesAsync(config.FilePath, context.CancellationToken))
                rowCount++;


            context.Logger.LogInformation("Counted {RowCount} rows in {FilePath}", rowCount, config.FilePath);

            return new TaskResult(true, ResultJson: JsonSerializer.Serialize(new { rowCount }));
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ErrorMessage: $"FILE_READ_FAILED: {ex.Message}");
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(path);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is not null) yield return line;
        }
    }

    public sealed class CountRowsConfig
    {
        public string? FilePath { get; set; }
    }
}

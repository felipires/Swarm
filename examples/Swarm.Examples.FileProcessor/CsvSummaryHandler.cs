using System.Text.Json;
using System.Text.Json.Serialization;
using Swarm.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Swarm.Examples.FileProcessor;

/// <summary>
/// Reads a CSV file from a local path, counts rows, and returns a summary.
///
/// Config shape (TaskDefinition.ConfigJson):
/// {
///   "filePath": "{param:filePath:required}",
///   "hasHeader": true
/// }
///
/// Runtime params (per-dispatch or from an upstream step's output mapping):
/// { "filePath": "/data/exports/users.csv" }
///
/// Result JSON:
/// { "rowCount": 1500, "columnCount": 8, "fileSizeBytes": 204800 }
/// </summary>
public sealed class CsvSummaryHandler : ITaskHandler
{
    public string TaskType => "csv-summary@1";

    public HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["filePath"],
              "properties": {
                "filePath":  { "type": "string" },
                "hasHeader": { "type": "boolean" }
              }
            }
            """,
        RequiredParams = ["filePath"],
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

        CsvSummaryConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<CsvSummaryConfig>(resolved, JsonOpts);
        }
        catch (JsonException ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_INVALID: {ex.Message}");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.FilePath))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: filePath is required");

        if (!File.Exists(config.FilePath))
            return new TaskResult(false, ErrorMessage: $"FILE_NOT_FOUND: {config.FilePath}");

        try
        {
            var fileInfo = new FileInfo(config.FilePath);
            int rowCount = 0;
            int columnCount = 0;

            await foreach (var line in ReadLinesAsync(config.FilePath, context.CancellationToken))
            {
                if (rowCount == 0)
                    columnCount = line.Split(',').Length;
                rowCount++;
            }

            // Subtract header row from data row count if present.
            int dataRows = config.HasHeader && rowCount > 0 ? rowCount - 1 : rowCount;

            var result = JsonSerializer.Serialize(new
            {
                rowCount = dataRows,
                columnCount,
                fileSizeBytes = fileInfo.Length,
                filePath = config.FilePath,
            });

            context.Logger.LogInformation(
                "CSV summary: {Rows} rows, {Cols} columns, {Bytes} bytes",
                dataRows, columnCount, fileInfo.Length);

            return new TaskResult(true, ResultJson: result);
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

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class CsvSummaryConfig
    {
        public string? FilePath { get; set; }
        public bool HasHeader { get; set; } = true;
    }
}

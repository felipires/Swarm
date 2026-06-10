using System.Text.Json;
using Swarm.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Swarm.Examples.DataPipeline;

/// <summary>
/// Receives aggregated values from upstream steps via output mappings and
/// writes a summary report to a file. Demonstrates how a downstream step
/// consumes multiple upstream results through runtime params.
///
/// Config shape:
/// {
///   "outputPath": "{param:outputPath:required}",
///   "labelA":     "Source A rows",
///   "labelB":     "Source B rows"
/// }
///
/// Runtime params (injected by output mappings from step-1 and step-2):
/// {
///   "outputPath": "/reports/summary.txt",
///   "n1": "1500",
///   "n2": "820"
/// }
///
/// Step params (authored in the pipeline editor):
/// {
///   "outputPath": "/reports/summary.txt",
///   "countA": "{param:n1}",2090
///   "countB": "{param:n2}"
/// }
///
/// Output mappings on this step:
///   rowCount → n1  from step-1
///   rowCount → n2  from step-2
/// </summary>
public sealed class AggregateReportHandler : TaskHandler<AggregateReportHandler.ReportConfig>
{
    public override string TaskType => "aggregate-report@1";

    public override HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["outputPath", "countA", "countB"],
              "properties": {
                "outputPath": { "type": "string" },
                "labelA":     { "type": "string" },
                "labelB":     { "type": "string" },
                "countA":     { "type": "string" },
                "countB":     { "type": "string" }
              }
            }
            """,
        RequiredParams = ["outputPath", "n1", "n2"],
    };

    protected override async Task<TaskResult> HandleAsync(ReportConfig config, TaskContext context)
    {
        if (string.IsNullOrWhiteSpace(config.OutputPath))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: outputPath is required");

        if (!long.TryParse(config.CountA, out var countA) ||
            !long.TryParse(config.CountB, out var countB))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: countA and countB must be numeric");

        try
        {
            var labelA = config.LabelA ?? "Source A";
            var labelB = config.LabelB ?? "Source B";
            var total = countA + countB;

            var report = $"""
                Aggregate Report — {DateTime.UtcNow:u}
                ─────────────────────────────────────
                {labelA,-30} {countA,10:N0}
                {labelB,-30} {countB,10:N0}
                {"Total",-30} {total,10:N0}
                """;

            var dir = Path.GetDirectoryName(config.OutputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(config.OutputPath, report, context.CancellationToken);

            context.Logger.LogInformation(
                "Report written to {OutputPath} (total={Total})", config.OutputPath, total);

            return new TaskResult(true, ResultJson: JsonSerializer.Serialize(new
            {
                outputPath = config.OutputPath,
                countA,
                countB,
                total,
            }));
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ErrorMessage: $"REPORT_WRITE_FAILED: {ex.Message}");
        }
    }

    public sealed class ReportConfig
    {
        public string? OutputPath { get; set; }
        public string? LabelA { get; set; }
        public string? LabelB { get; set; }
        public string? CountA { get; set; }
        public string? CountB { get; set; }
    }
}

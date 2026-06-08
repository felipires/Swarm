using System.Net.Http.Json;
using System.Text.Json;
using Swarm.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Swarm.Examples.SlackNotifier;

/// <summary>
/// Posts a message to a Slack channel via Incoming Webhook.
/// Demonstrates the env: source for secrets and {param:...} for dynamic text.
///
/// Config shape (TaskDefinition.ConfigJson):
/// {
///   "webhookUrl": "{env:SLACK_WEBHOOK_URL:secret:required}",
///   "channel":   "#ops-alerts",
///   "text":      "{param:message:required}",
///   "username":  "Swarm"
/// }
///
/// Runtime params (per-dispatch):
/// { "message": "Pipeline 'nightly-sync' completed: 1 500 rows processed." }
///
/// The SLACK_WEBHOOK_URL env key must be present in the Node's env store
/// (delivered via POST /api/nodes/:id/env or set as SWARM_TASKENV_SLACK_WEBHOOK_URL).
/// </summary>
public sealed class SlackNotifyHandler : ITaskHandler
{
    private static readonly HttpClient HttpClient = new();

    public string TaskType => "slack-notify@1";

    public HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["webhookUrl", "text"],
              "properties": {
                "webhookUrl": { "type": "string" },
                "channel":   { "type": "string" },
                "text":      { "type": "string" },
                "username":  { "type": "string" },
                "iconEmoji": { "type": "string" }
              }
            }
            """,
        RequiredEnvKeys = ["SLACK_WEBHOOK_URL"],
        RequiredParams = ["message"],
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

        SlackConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<SlackConfig>(resolved, JsonOpts);
        }
        catch (JsonException ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_INVALID: {ex.Message}");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.WebhookUrl))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: webhookUrl is required");
        if (string.IsNullOrWhiteSpace(config.Text))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: text is required");

        var payload = new
        {
            text = config.Text,
            channel = config.Channel,
            username = config.Username ?? "Swarm",
            icon_emoji = config.IconEmoji ?? ":robot_face:",
        };

        try
        {
            using var response = await HttpClient.PostAsJsonAsync(
                config.WebhookUrl, payload, context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
                return new TaskResult(false,
                    ErrorMessage: $"SLACK_ERROR: {(int)response.StatusCode} — {body}");
            }

            context.Logger.LogInformation("Slack notification sent to {Channel}", config.Channel);
            return new TaskResult(true, ResultJson: JsonSerializer.Serialize(new
            {
                channel = config.Channel,
                text = config.Text,
            }));
        }
        catch (Exception ex)
        {
            return new TaskResult(false, ErrorMessage: $"SLACK_REQUEST_FAILED: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class SlackConfig
    {
        public string? WebhookUrl { get; set; }
        public string? Channel { get; set; }
        public string? Text { get; set; }
        public string? Username { get; set; }
        public string? IconEmoji { get; set; }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Handlers;

/// <summary>
/// Built-in <c>webhook@1</c> handler — POSTs the run context as JSON with
/// an HMAC-SHA256 signature header (<c>X-Swarm-Signature</c>). Consumers
/// verify by recomputing HMAC-SHA256 over the request body with the shared
/// secret.
/// </summary>
public sealed class WebhookHandlerV1 : TaskHandler<WebhookHandlerV1.WebhookHandlerConfig>
{
    private static readonly HttpClient HttpClient = new();

    public override string TaskType => "webhook@1";

    public override HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["url", "secret"],
              "properties": {
                "url": { "type": "string" },
                "secret": { "type": "string" },
                "payload": { "type": "object" },
                "timeoutSeconds": { "type": "integer", "minimum": 1 },
                "signatureHeader": { "type": "string" }
              }
            }
            """
    };

    protected override async Task<TaskResult> HandleAsync(WebhookHandlerConfig config, TaskContext context)
    {
        if (string.IsNullOrEmpty(config.Url) || string.IsNullOrEmpty(config.Secret))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: url and secret are required");

        var payload = new
        {
            instanceId = context.Message.InstanceId,
            taskType = context.Message.TaskType,
            payload = config.Payload.HasValue ? (object)config.Payload.Value : new { },
        };
        var body = JsonSerializer.Serialize(payload);
        var signature = ComputeSignature(body, config.Secret);
        var headerName = string.IsNullOrEmpty(config.SignatureHeader) ? "X-Swarm-Signature" : config.SignatureHeader;

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(headerName, signature);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds <= 0 ? 30 : config.TimeoutSeconds));

        try
        {
            using var response = await HttpClient.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            var resultJson = JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                body = responseBody,
            });
            return response.IsSuccessStatusCode
                ? new TaskResult(true, ResultJson: resultJson)
                : new TaskResult(false, ResultJson: resultJson,
                    ErrorMessage: $"WEBHOOK_UNEXPECTED_STATUS: {(int)response.StatusCode}");
        }
        catch (TaskCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return new TaskResult(false, ErrorMessage: "WEBHOOK_TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            return new TaskResult(false, ErrorMessage: $"WEBHOOK_REQUEST_FAILED: {ex.Message}");
        }
    }

    private static string ComputeSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public sealed class WebhookHandlerConfig
    {
        public string? Url { get; set; }
        public string? Secret { get; set; }
        public JsonElement? Payload { get; set; }
        public int TimeoutSeconds { get; set; }
        public string? SignatureHeader { get; set; }
    }
}

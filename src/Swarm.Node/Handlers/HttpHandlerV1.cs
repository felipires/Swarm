using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Swarm.Sdk.Abstractions;

namespace Swarm.Node.Handlers;

/// <summary>
/// Built-in <c>http@1</c> handler. Resolves placeholders in the config blob,
/// makes an HTTP request, and returns the response. One <see cref="HttpClient"/>
/// is shared across invocations via the static field — DNS rotation is fine
/// over the multi-hour lifetime of a Node, but if it becomes a problem the
/// container can be cycled.
/// </summary>
public sealed class HttpHandlerV1 : ITaskHandler
{
    private static readonly HttpClient HttpClient = new();

    public string TaskType => "http@1";

    public HandlerSchema Schema { get; } = new()
    {
        JsonSchema = """
            {
              "type": "object",
              "required": ["method", "url"],
              "properties": {
                "method": { "type": "string" },
                "url": { "type": "string" },
                "headers": { "type": "object" },
                "body": { "type": "string" },
                "successStatusCodes": { "type": "array", "items": { "type": "integer" } },
                "timeoutSeconds": { "type": "integer", "minimum": 1 }
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

        HttpHandlerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<HttpHandlerConfig>(resolved, JsonOpts);
        }
        catch (JsonException ex)
        {
            return new TaskResult(false, ErrorMessage: $"CONFIG_RESOLUTION_INVALID: {ex.Message}");
        }

        if (config is null || string.IsNullOrEmpty(config.Url))
            return new TaskResult(false, ErrorMessage: "CONFIG_RESOLUTION_INVALID: url is required");

        using var request = new HttpRequestMessage(new HttpMethod(config.Method ?? "GET"), config.Url);
        if (config.Body is not null)
            request.Content = new StringContent(config.Body, Encoding.UTF8, "application/json");

        if (config.Headers is { Count: > 0 })
        {
            foreach (var (k, v) in config.Headers)
            {
                if (!request.Headers.TryAddWithoutValidation(k, v))
                    request.Content?.Headers.TryAddWithoutValidation(k, v);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds <= 0 ? 30 : config.TimeoutSeconds));

        try
        {
            using var response = await HttpClient.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            var statusOk = config.SuccessStatusCodes is { Length: > 0 }
                ? config.SuccessStatusCodes.Contains((int)response.StatusCode)
                : response.IsSuccessStatusCode;

            var resultJson = JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                body,
            });

            return statusOk
                ? new TaskResult(true, ResultJson: resultJson)
                : new TaskResult(false, ResultJson: resultJson,
                    ErrorMessage: $"HTTP_UNEXPECTED_STATUS: {(int)response.StatusCode}");
        }
        catch (TaskCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return new TaskResult(false, ErrorMessage: "HTTP_TIMEOUT");
        }
        catch (HttpRequestException ex)
        {
            return new TaskResult(false, ErrorMessage: $"HTTP_REQUEST_FAILED: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class HttpHandlerConfig
    {
        public string? Method { get; set; }
        public string? Url { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
        public int[]? SuccessStatusCodes { get; set; }
        public int TimeoutSeconds { get; set; }
    }
}

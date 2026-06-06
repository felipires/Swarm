---
title: Writing Handlers
---

# Writing Custom Task Handlers

## 1. Create a class library

```bash
dotnet new classlib -n MyHandlers
dotnet add MyHandlers/MyHandlers.csproj package Swarm.Node.Sdk
```

## 2. Implement `ITaskHandler`

```csharp
using Swarm.Sdk.Abstractions;
using System.Text.Json;

public class SendEmailHandler : ITaskHandler
{
    public string TaskType => "send-email@1";

    public HandlerSchema Schema => new HandlerSchema
    {
        JsonSchema = """
        {
          "type": "object",
          "required": ["to", "subject"],
          "properties": {
            "to":      { "type": "string" },
            "subject": { "type": "string" },
            "body":    { "type": "string" }
          }
        }
        """,
        RequiredEnvKeys = new[] { "SMTP_PASSWORD" },
        RequiredParams = new[] { "recipient" }
    };

    public async Task<TaskResult> HandleAsync(TaskContext context, CancellationToken ct)
    {
        // context.Config is the fully-resolved configJson as a JsonElement
        var to = context.Config.GetProperty("to").GetString()!;
        var subject = context.Config.GetProperty("subject").GetString()!;

        // Resolved env values are available via context.Resolver
        // Secrets resolved with :secret modifier are redacted in logs

        context.Logger.LogInformation("Sending email to {To}", to);

        // ... send the email ...

        return new TaskResult(
            Success: true,
            ResultJson: JsonSerializer.Serialize(new { delivered = true, to })
        );
    }
}
```

## 3. Register the handler

In the Node's `Program.cs`:

```csharp
services.AddTaskHandler<SendEmailHandler>();
```

Or from a plugin assembly (see [Plugins →](plugins)).

## `ITaskHandler` interface

```csharp
public interface ITaskHandler
{
    string TaskType { get; }
    HandlerSchema Schema { get; }
    Task<TaskResult> HandleAsync(TaskContext context, CancellationToken ct);
}
```

## `TaskContext` properties

| Property | Type | Description |
|---|---|---|
| `Config` | `JsonElement` | Fully-resolved task config (placeholders replaced) |
| `Logger` | `ILogger` | Scoped logger; secrets are redacted automatically |
| `Resolver` | `ValueResolverPipeline` | Access to the raw resolver if needed |
| `CancellationToken` | `CancellationToken` | Propagated from the Node's shutdown token |

## `TaskResult`

```csharp
public record TaskResult(
    bool Success,
    string? ResultJson = null,   // arbitrary JSON string
    string? ErrorMessage = null  // populated on failure
);
```

Return `Success: false` with an `ErrorMessage` to mark the instance `Failed` and trigger retries if configured.

## `HandlerSchema`

```csharp
public class HandlerSchema
{
    public string JsonSchema { get; init; } = "{}";       // JSON Schema string
    public IReadOnlyList<string> RequiredEnvKeys { get; init; } = [];
    public IReadOnlyList<string> RequiredParams { get; init; } = [];
}
```

The Cluster uses `RequiredEnvKeys` and `RequiredParams` to validate dispatches before they reach the Node.

## Error handling

Unhandled exceptions in `HandleAsync` are caught by the executor, which marks the instance `Failed` with the exception message. Always return `TaskResult(false, ErrorMessage: ...)` for expected failure paths so the error message is meaningful.

---
title: Writing Handlers
---

# Writing Custom Task Handlers

## 1. Create a class library

```bash
dotnet new classlib -n MyHandlers
dotnet add MyHandlers/MyHandlers.csproj package Swarm.Node.Sdk
```

## 2. Implement a handler

There are two ways to write a handler. They differ only in who deserializes the
config — the Node core resolves placeholders and validates the dispatch either way.

| | When to use |
|---|---|
| **`TaskHandler<TConfig>`** (recommended) | You have a strongly-typed config DTO. The base deserializes the resolved config for you and eager-returns `CONFIG_INVALID` on a bad shape. |
| **`ITaskHandler`** (low-level) | You need the raw resolved `JsonElement` (dynamic/loosely-typed config), or no config at all (e.g. a passthrough). |

In both cases the config handed to your handler is **already resolved**: every
`{env:…}` / `{param:…}` / `{config:…}` placeholder — including value-position ones
like `{param:timeout:type=int}` and `{param:headers:type=json}` — has been
substituted and re-parsed by the Node core before your code runs. You never call
the resolver for the config yourself. See [Config placeholders →](../concepts/config-placeholders.md).

### Option A — `TaskHandler<TConfig>` (recommended)

Subclass `TaskHandler<TConfig>` and implement the typed overload. The base owns the
deserialize-and-validate boilerplate.

```csharp
using Swarm.Sdk.Abstractions;
using System.Text.Json;

public sealed class SendEmailHandler : TaskHandler<SendEmailHandler.Config>
{
    public override string TaskType => "send-email@1";

    public override HandlerSchema Schema { get; } = new()
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
        RequiredEnvKeys = ["SMTP_PASSWORD"],
        RequiredParams = ["recipient"],
    };

    // `config` is the resolved configJson, already deserialized into Config.
    protected override async Task<TaskResult> HandleAsync(Config config, TaskContext context)
    {
        if (string.IsNullOrWhiteSpace(config.To))
            return new TaskResult(false, ErrorMessage: "CONFIG_INVALID: to is required");

        context.Logger.LogInformation("Sending email to {To}", config.To);

        // ... send the email ...

        return new TaskResult(
            Success: true,
            ResultJson: JsonSerializer.Serialize(new { delivered = true, to = config.To }));
    }

    // Must be public — it's the generic type argument on a public handler.
    public sealed class Config
    {
        public string? To { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }
}
```

Deserialization defaults to case-insensitive property matching. Override
`ConfigJsonOptions` if you need custom `JsonSerializerOptions`.

### Option B — `ITaskHandler` (low-level)

Implement `ITaskHandler` directly when you want the raw resolved `JsonElement` or
have no config to parse.

```csharp
using Swarm.Sdk.Abstractions;
using System.Text.Json;

public sealed class PingHandler : ITaskHandler
{
    public string TaskType => "ping@1";
    public HandlerSchema Schema { get; } = new();

    public Task<TaskResult> HandleAsync(TaskContext context)
    {
        // context.Config is the fully-resolved configJson as a JsonElement.
        var label = context.Config.TryGetProperty("label", out var l) ? l.GetString() : "ping";
        context.Logger.LogInformation("Ping: {Label}", label);

        return Task.FromResult(new TaskResult(
            Success: true,
            ResultJson: JsonSerializer.Serialize(new { pong = true, label })));
    }
}
```

## 3. Register the handler

In the Node's `Program.cs` (works for both options — `TaskHandler<T>` implements `ITaskHandler`):

```csharp
services.AddTaskHandler<SendEmailHandler>();
```

Or from a plugin assembly (see [Plugins →](plugins)).

## `TaskContext` properties

| Property | Type | Description |
|---|---|---|
| `Message` | `TaskMessage` | The wire message that triggered the invocation (instance/def/run ids, task type). |
| `Config` | `JsonElement` | Fully-resolved task config (all placeholders substituted, re-parsed). |
| `RuntimeParams` | `JsonElement` | Resolved per-dispatch params, or an undefined element if absent. |
| `Resolver` | `ValueResolverPipeline` | The resolver, exposed for resolving *additional* dynamic strings at runtime (the config is already done for you). |
| `Logger` | `ILogger` | Scoped logger; `:secret` values are redacted automatically. |
| `CancellationToken` | `CancellationToken` | Propagated from the Node's shutdown token. |

`context.GetConfig<T>(options?)` deserializes `Config` into your DTO — this is what
`TaskHandler<TConfig>` uses internally, and it's available on the context for
`ITaskHandler` implementations too.

## Who validates what

- **Cluster**, before dispatch: parses the config (placeholder-aware), checks it
  against your `Schema.JsonSchema`, and verifies `RequiredEnvKeys` / `RequiredParams`.
  Value-position `{…:type=json}` fields are runtime-typed and exempt from the
  Cluster type-check.
- **Node core**, before your handler: resolves every placeholder. A resolution
  failure fails the task with `CONFIG_RESOLUTION_FAILED` (missing required /
  coercion error) or `CONFIG_RESOLUTION_INVALID` (resolved text isn't JSON) —
  your handler is never invoked in those cases.
- **Your handler**: receives a resolved, (optionally) typed config and applies its
  own semantic checks. Use `CONFIG_INVALID` for shape/value problems specific to
  the handler.

## `TaskResult`

```csharp
public record TaskResult(
    bool Success,
    string? ResultJson = null,   // arbitrary JSON string
    string? ErrorMessage = null  // populated on failure
);
```

Return `Success: false` with an `ErrorMessage` to mark the instance `Failed` and
trigger retries if configured.

## `HandlerSchema`

```csharp
public class HandlerSchema
{
    public string JsonSchema { get; init; } = "{}";       // JSON Schema string
    public IReadOnlyList<string> RequiredEnvKeys { get; init; } = [];
    public IReadOnlyList<string> RequiredParams { get; init; } = [];
}
```

## Error handling

Unhandled exceptions in `HandleAsync` are caught by the executor, which marks the
instance `Failed` with the exception message. Prefer returning
`TaskResult(false, ErrorMessage: ...)` for expected failure paths so the error
message is meaningful, and keep handlers idempotent — the broker may redeliver.
```

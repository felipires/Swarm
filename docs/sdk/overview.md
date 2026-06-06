---
title: Node SDK Overview
---

# Node SDK

The `Swarm.Node.Sdk` package (NuGet ID: `Swarm.Node.Sdk`, C# namespace: `Swarm.Sdk.*`) is the handler-authoring surface for task implementations.

## What the SDK provides

| Component | Namespace | Description |
|---|---|---|
| `ITaskHandler` | `Swarm.Sdk.Abstractions` | Interface all handlers implement |
| `HandlerSchema` | `Swarm.Sdk.Abstractions` | JSON Schema + required env/param declarations |
| `TaskContext` | `Swarm.Sdk.Abstractions` | Execution context: resolved config, logger, cancellation |
| `TaskResult` | `Swarm.Sdk.Abstractions` | Handler return value |
| `TaskMessage` | `Swarm.Sdk.Wire` | Wire message shape (shared with Cluster) |
| `IValueResolver` / `ValueResolverPipeline` | `Swarm.Sdk.ValueResolution` | Placeholder resolution pipeline |
| `Placeholder` / `PlaceholderParser` | `Swarm.Sdk.ValueResolution` | Placeholder parsing |
| `TaskTypeId` | `Swarm.Sdk` | `name@version` parsing and validation |
| `AddTaskHandler<T>()` | `Swarm.Sdk.DependencyInjection` | DI registration extension |

## Built-in handlers

The Node ships four built-in handlers:

| Task type | Description |
|---|---|
| `default@1` | Pass-through; returns the config as the result |
| `http@1` | HTTP request (GET, POST, PUT, PATCH, DELETE) with headers, body, timeout |
| `sql@1` | SQL query execution (Postgres, SQL Server, MySQL) |
| `webhook@1` | HTTP POST with HMAC-SHA256 signature |

## Next steps

- [Writing custom handlers →](writing-handlers)
- [Value resolution →](value-resolution)
- [Plugin loading →](plugins)

---
title: Value Resolution
---

# Value Resolution

The `ValueResolverPipeline` resolves `{tier:KEY}` placeholders in task config before `HandleAsync` is called.

## Resolution order

For each `{env:KEY}` placeholder:

1. `SWARM_TASKENV_<KEY>` environment variable (Tier 1)
2. Node's encrypted SQLite store (Tier 2) — values delivered via `POST /api/nodes/{id}/env`

For each `{param:KEY}` placeholder:

3. `runtimeParams` from the dispatch request (Tier 3)

## Modifier reference

```
{env:KEY}                        → resolve KEY
{env:KEY:required}               → throw if absent
{env:KEY:secret}                 → resolve + redact in logs
{env:KEY:default=fallback}       → use "fallback" if absent
{env:KEY:type=int}               → parse as integer
{env:KEY:type=float}             → parse as float
{env:KEY:type=bool}              → parse as boolean
{env:KEY:type=json}              → inject as raw JSON value
{env:KEY:required:secret}        → chain multiple modifiers
{param:KEY:default=100:type=int} → chain default + type
```

## Accessing resolved values in a handler

The resolved config is passed as `context.Config` (a `JsonElement`). Placeholders are replaced in-place before the element is parsed:

```csharp
var batchSize = context.Config.GetProperty("batch_size").GetInt32(); // already an int
var connStr = context.Config.GetProperty("connection").GetString();  // already a string
```

For raw resolver access:

```csharp
var resolved = await context.Resolver.ResolveAsync("env", "DATABASE_URL");
if (resolved.IsSecret) { /* treat carefully */ }
var value = resolved.Value;
```

## Secret redaction in logs

When a placeholder is marked `:secret`, the resolved value is tracked by `SecretRedactionContext`. The `SecretRedactionEnricher` walks every `LogEvent` and replaces any string property matching a tracked secret with `[REDACTED]`.

This works for **parameterized** log calls:

```csharp
// ✓ Redacted: the value is a separate property
context.Logger.LogInformation("Connecting to {Url}", connectionString);

// ✗ Not redacted: value is baked into the message template
context.Logger.LogInformation($"Connecting to {connectionString}");
```

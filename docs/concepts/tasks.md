---
title: Tasks
---

# Tasks

## TaskDefinition

A `TaskDefinition` is a reusable template. It specifies:

- **Task type** — the handler implementation to invoke (`name@version`)
- **Config** — a JSON payload with optional placeholders
- **Default routing** — dispatch strategy and target selector
- **Retry policy** — max retries, delay, and backoff

Definitions are edited freely. Each edit increments `version`. Task instances snapshot both `taskType` and `configJson` at dispatch time — later edits cannot alter in-flight executions.

## TaskInstance

Dispatching creates a `TaskInstance`. It is an immutable execution record:

| Field | Description |
|---|---|
| `id` | Instance UUID |
| `taskDefinitionId` | The definition it was created from |
| `nodeId` | Assigned Node (null until claimed for shared-queue tasks) |
| `taskType` | Snapshotted from the definition at dispatch |
| `configJsonSnapshot` | Snapshotted config |
| `runtimeParamsJson` | Per-dispatch parameters |
| `status` | Current lifecycle status |
| `retryCount` | Number of retries attempted |

## Instance status lifecycle

```
Pending ──► Dispatched ──► Running ──► Completed
                                   └──► Failed
                                         │
                         (retry)         ▼
                                       Pending (retryAfter set)
```

| Status | Meaning |
|---|---|
| `Pending` | Created; not yet published to broker. Also the retry-wait state. |
| `Claimed` | Claimed from a shared queue (transitional) |
| `Dispatched` | Published to RabbitMQ |
| `Running` | Node has started execution |
| `Completed` | Node reported success |
| `Failed` | Node reported failure |

## Config placeholders

`configJson` supports two resolution tiers:

```json
{
  "connection": "{env:DATABASE_URL}",
  "table": "{param:target_table}",
  "mode": "append"
}
```

| Placeholder | Source | Resolved at |
|---|---|---|
| `{env:KEY}` | Node's encrypted local env store | Node, before handler invocation |
| `{param:KEY}` | Per-dispatch `runtimeParams` | Node, before handler invocation |

Modifiers are supported: `{env:KEY:required}`, `{param:KEY:default=fallback}`, `{env:KEY:type=int}`.

See [Value Resolution →](config-placeholders) for the full reference.

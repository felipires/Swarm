---
title: Observability
---

# Observability

Swarm collects structured logs from every Node and pipeline execution into a searchable, persistent store. The Observability page and the `GET /api/logs` endpoint provide a Datadog-style query interface over that store.

## How logs flow

```
Node process
  └─ Serilog (structured) → DynamicRabbitMqSink
       └─ RabbitMQ `logs` queue
            └─ Cluster LogConsumerService (per-node buffer, 5 s flush)
                 └─ Postgres Logs table (jsonb Tags, pg_trgm free-text)
```

The Node attaches a `SwarmTags` property to every log emitted during a task execution. The Cluster reads that property at ingest and stores it in the `Tags` jsonb column alongside the node's current routing tags (see [Tags](./tags.md)), so the full context is always present on the log row.

## Correlation tags

Every log row carries a `Tags` jsonb object. Well-known keys set by the system:

| Key | Value | Set by |
|---|---|---|
| `task` | TaskInstance ID | Node (per task execution) |
| `taskDef` | TaskDefinition ID | Node |
| `taskType` | Task type string | Node |
| `run` | PipelineRun ID | Node (via wire message) + Cluster lifecycle events |
| `step` | PipelineStep ID | Node + Cluster lifecycle events |
| `pipeline` | Pipeline ID | Node + Cluster lifecycle events |
| `env.<KEY>` | Process env value | Node — opt-in allow-list only (see below) |
| *(node routing tags)* | e.g. `region`, `env`, `tier` | Cluster at ingest — merged from `effectiveTagsJson` |

Node routing tags (static + overlay) are merged as the base layer; log-level SwarmTags are merged on top, so a node tag named `task` can never shadow an actual task ID.

### Env-key tagging

To tag logs with specific process environment variables, add the key names to `Logging:LogTagEnvKeys` in the Node's `appsettings.json`:

```json
{
  "Logging": {
    "LogTagEnvKeys": ["DEPLOY_ENV", "REGION"]
  }
}
```

These appear as `env.DEPLOY_ENV` and `env.REGION` in the Tags. The encrypted secrets store is never read for tags — only process environment variables in the explicit allow-list are included.

## Pipeline lifecycle logs

`PipelineRunExecutor` writes a log row at each transition:

| Event | Tags present |
|---|---|
| Pipeline run started | `run`, `pipeline` |
| Step dispatched to node | `run`, `step`, `pipeline` |
| Step completed | `run`, `step`, `pipeline` |
| Step failed | `run`, `step`, `pipeline` |
| Step skipped | `run`, `step`, `pipeline` |
| Pipeline run completed / failed | `run`, `pipeline` |

These cluster-origin rows have a `null` NodeId. Combined with node task logs, `run:<id>` returns a complete orchestration + execution timeline.

## Querying logs

### Query bar syntax

The Observability search bar accepts inline facets and free text:

```
run:9c2b level:>Warning "connection reset"
region:eu task:7f3a
```

| Token form | Meaning |
|---|---|
| `key:value` | Tag containment — matches logs where `Tags` contains `{key: value}` |
| `node:<id>` | Filter by NodeId column directly |
| `level:<name>` | Exact level (e.g. `level:Error`) |
| `level:>Warning` | Warning and above (Warning, Error, Fatal) |
| `level:<Error` | Error and below (Verbose … Error) |
| `"quoted text"` | Free-text substring over message and messageTemplate |
| bare word | Same as quoted text |

Multiple facets are AND-combined. Multiple `level:` tokens are OR-combined.

### Time range and auto-refresh

- **Time range**: Last 15 min / 1 h / 24 h / All time
- **Auto-refresh**: Off / 5 s / 15 s — polls `GET /api/logs` on an interval with no held connection

### Logs in the pipeline view

- **Run logs tab**: shows all logs tagged `run:<selectedRunId>` — orchestration lifecycle plus node task execution — as a chronological timeline.
- **Step logs panel**: in the step detail sidebar, logs tagged `step:<stepId>` show the node's execution log for that specific step.

## Log retention

`LogRetentionService` purges entries older than `Logging:RetentionDays` (default 30 days). Set to `0` to disable.

---
title: Nodes
---

# Nodes

A Node is a worker process that executes tasks. Nodes self-register with the Cluster and are tracked by heartbeat.

## Lifecycle

```
startup → RegisterNode (gRPC) → receive broker creds
        → begin heartbeat loop (every Heartbeat:IntervalSeconds)
        → receive work via RabbitMQ
        → report results
```

If a Node stops heartbeating for `Heartbeat:TimeoutSeconds` (default 300s), the Cluster marks it `Offline`. Its queued work is not automatically redistributed — operators can re-dispatch manually.

## Identity

Each Node generates a UUID on first start and persists it to its local SQLite database (`NodeIdentityResolver`). The same ID is reused across restarts. If the database is deleted, the Node registers as a new entry in the Cluster.

## Static tags

A Node reports its static tags at registration. These come from:
- Environment variables: `SWARM_TAG_<key>=<value>`
- `appsettings.json` under `Swarm:Tags`

Static tags are immutable after registration. They combine with operator-set overlay tags to form the **effective tag set** used for routing.

## Live metrics (P5-1)

Every heartbeat includes a live snapshot:

| Metric | Description |
|---|---|
| `cpuPercent` | Process CPU usage as a percentage of all cores |
| `memoryUsedBytes` | Process working set |
| `memoryAvailableBytes` | System-available memory |
| `inFlightTasks` | Tasks currently executing on this Node |
| `uptimeSeconds` | Seconds since the Node process started |
| `health` | `Healthy` / `Degraded` / `Unhealthy` |

Health thresholds:

| Status | CPU | Memory ratio |
|---|---|---|
| `Healthy` | < 85% | < 90% |
| `Degraded` | ≥ 85% | ≥ 90% |
| `Unhealthy` | ≥ 95% | ≥ 97% |

Metrics are stored in Redis with a 5-minute TTL and are best-effort — Redis unavailability never fails a heartbeat.

## Capabilities

At registration, each Node reports the task types it can handle (`name@version`). These are stored as `NodeCapability` rows and exposed via `GET /api/nodes/{id}` in the `capabilities` array.

The Cluster uses capability lists for dispatch validation — it warns (but does not reject) when dispatching to a task type that no current Node advertises.

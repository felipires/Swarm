---
title: Architecture
---

# Architecture

## Components

### Cluster

The Cluster is an ASP.NET Core 8 application. It is the single control plane for the entire fleet.

**Responsibilities:**
- Persist all durable state (task definitions, pipeline graphs, runs, schedules, node registry) in Postgres
- Accept Node registrations and heartbeats via gRPC
- Route dispatched tasks to Nodes via RabbitMQ
- Consume task results and advance pipeline state
- Stream Node logs to operator clients via SSE
- Store per-node live metrics in Redis (TTL-based, self-expiring)

**Internal services:**

| Service | Type | Role |
|---|---|---|
| `HeartbeatBackgroundService` | Hosted | Marks nodes offline when heartbeat lapses |
| `OutboxPublisherService` | Hosted | Publishes pending dispatches from the outbox table |
| `TaskResultConsumerService` | Hosted | Reads from `task-results` queue, advances FSM |
| `TaskClaimsConsumerService` | Hosted | Reads from `task-claims`, binds shared-queue instances to a Node |
| `LogConsumerService` | Hosted | Reads from `swarm.node.logs`, buffers, fans out to SSE |
| `RetrySchedulerService` | Hosted | Re-dispatches eligible failed instances after delay |
| `SchedulerService` | Hosted | Fires cron-scheduled pipeline runs |
| `InProcessStepAdvancer` | Hosted | Advances pipeline DAG after each step result |
| `LogRetentionService` | Hosted | Purges old log entries per `Logging:RetentionDays` |
| `TaggedRouteRetentionService` | Hosted | Purges stale tagged-route rows |

### Node

The Node is a .NET Worker SDK process. Any number of Nodes can be connected to a single Cluster.

**Responsibilities:**
- Register with the Cluster and receive broker credentials
- Execute task handlers registered via the SDK
- Report results, claim shared-queue tasks, emit structured logs
- Apply overlay tags and env-secret ops delivered by heartbeat responses
- Collect and report CPU/memory/health metrics on every heartbeat

### Message flow

```
POST /api/tasks/{id}/dispatch
        │
        ▼
TaskDispatchService
        │
        ▼ writes
PendingDispatch row (outbox)
        │
OutboxPublisherService (polls every 1s)
        │
        ▼ publishes to RabbitMQ
┌────────────────────────────┐
│   tasks.node.<nodeId>      │  SpecificNode
│   tasks.shared.<taskType>  │  AnyOnlineNode / TaggedNodes
│   tasks.node.*  (fan-out)  │  AllOnlineNodes
└────────────────────────────┘
        │
        ▼ consumes
     Node
        │
        ▼
TaskExecutorService.DispatchAsync()
        │
        ▼
  handler.HandleAsync()
        │
        ▼ publishes to
  task-results queue
        │
        ▼ consumed by
TaskResultConsumerService
        │
        ▼ updates
TaskInstance.Status → Completed / Failed
        │
        ▼ (if pipeline)
InProcessStepAdvancer → next step
```

## Data stores

| Store | What lives there |
|---|---|
| **Postgres** | All durable state — nodes, task defs, instances, pipelines, runs, schedules, logs, overlay tags, env ops, outbox |
| **RabbitMQ** | In-flight task messages, result acks, claims, log events |
| **Redis** | Per-node live metrics (TTL-based, ephemeral) |
| **Node SQLite** | Node identity, encrypted env-secret store |

## Key design decisions

| ID | Decision |
|---|---|
| D1 | Hybrid queue topology — per-node queue + shared queue per task type |
| D2 | Single Cluster instance; `IStepAdvancer` abstraction reserved for HA |
| D3 | `TaskType = name@version`; schema changes require a new version |
| D4 | Cluster validates leniently (placeholder-aware); Node validates strictly |
| D5 | Warn but don't reject if no Node currently handles the TaskType |
| D6 | Static tags (Node-reported) + overlay tags (operator-set); static wins on conflict |
| D7 | REST and gRPC authentication deferred to P4-1 |

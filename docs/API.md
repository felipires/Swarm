# Swarm API — Developer Documentation

> Complete reference for the Swarm Cluster REST API. Covers all features,
> their concepts, full request/response shapes, payload examples, error codes,
> and how features compose together in real workflows.

**Base URL:** `http://localhost:5001/api`  
**API Key header:** `X-API-Key: <key>` (required on authenticated endpoints)  
**Content-Type:** `application/json` for all request bodies  
**All timestamps:** ISO 8601 UTC (e.g. `"2024-11-12T09:41:03.123Z"`)  
**All IDs:** UUID v4 strings

---

## Table of Contents

1. [Concepts](#concepts)
2. [Common Shapes](#common-shapes)
3. [Nodes](#nodes)
4. [Tasks](#tasks)
5. [Dispatch](#dispatch)
6. [Pipelines](#pipelines)
7. [Pipeline Runs](#pipeline-runs)
8. [Schedules](#schedules)
9. [Logs](#logs)
10. [Health](#health)
11. [Cross-Feature Workflows](#cross-feature-workflows)
12. [Error Reference](#error-reference)

---

## Concepts

### Cluster and Nodes

Swarm has one **Cluster** (this API) and any number of **Nodes** (worker
processes). Nodes register themselves on startup via gRPC, then send periodic
heartbeats. The Cluster tracks each Node's liveness through `status` and
`lastHeartbeatAt`. Nodes that stop heartbeating are eventually flipped to
`Offline` by the Cluster's heartbeat background service.

### Tasks and Instances

A **TaskDefinition** is a reusable template: it names a task type, carries a
default config payload, and sets retry/backoff policy. Dispatching a task
creates a **TaskInstance** — an immutable execution record that snapshots the
definition's config and version at dispatch time. The Node receives the
instance, executes it, and reports the result back. A definition can be edited
freely; in-flight instances always execute against the snapshot taken at
dispatch.

### Pipelines

A **Pipeline** is a directed acyclic graph of steps, each step backed by a
TaskDefinition. Steps declare dependencies by name (`dependsOn`). The Cluster
runs a DAG executor that dispatches each step as its dependencies complete,
tracks step-level and run-level status, and handles failures according to each
step's `failurePolicy`.

### Dispatch Strategies

How a task is routed to a Node is determined by its `DispatchStrategy`.

| Strategy | Routing | Notes |
|---|---|---|
| `SpecificNode` | Named node by ID | `nodeId` required at dispatch |
| `AnyOnlineNode` | Any node via shared queue | Competing consumers; `NodeId` starts null, set after claim |
| `AllOnlineNodes` | Every online node | Creates one instance per node |
| `TaggedNodes` | Nodes matching a tag selector | Cluster resolves eligible nodes at dispatch time |

### Tags

Each Node has two tag layers:
- **Static tags**: set by the Node at registration (`StaticTagsJson`). Read-only after registration.
- **Overlay tags**: set by the Cluster operator via `PATCH /api/nodes/{id}/tags`. Override static tags on key conflict.

The **effective tag set** (static ∪ overlay, static wins on conflict) is the
authoritative routing surface. It is denormalized into a GIN-indexed JSONB
column for fast containment queries (`@>` selector matching).

### Task Type and Versioning

Every TaskDefinition has a `taskType` string (format `name@version`, e.g.
`"http@1"`). The Node SDK maps task types to handler implementations. When a
definition is edited, its `version` increments. Task instances snapshot both
`taskType` and `configJson` at dispatch — later definition edits cannot alter
in-flight executions.

### Schedules

A **Schedule** attaches a cron expression to a Pipeline. The Cluster's
`SchedulerService` sweeps enabled schedules every 10 seconds and fires runs
whose `nextFireAt` has passed. Cron supports both 5-field (standard) and
6-field (with seconds) expressions.

### Retry Policy

Failed TaskInstances can retry automatically based on the definition's
`maxRetries`, `retryDelaySeconds`, and `retryBackoff` settings. The
`RetrySchedulerService` re-dispatches eligible failed instances after their
computed delay:

| `retryBackoff` | Delay formula |
|---|---|
| `Fixed` | `retryDelaySeconds` |
| `Linear` | `retryDelaySeconds × attempt` |
| `Exponential` | `retryDelaySeconds × 2^(attempt−1)` |

Maximum delay is capped at 86400 seconds (24h) regardless of backoff.

---

## Common Shapes

### PagedResult

Offset-based pagination response, used on most list endpoints.

```json
{
  "items": [...],
  "total": 42,
  "page": 1,
  "pageSize": 20
}
```

**Query parameters:**

| Param | Default | Max | Description |
|---|---|---|---|
| `page` | `1` | — | 1-based page number |
| `pageSize` | `20` | `100` | Items per page |

### CursorPagedResult

Keyset pagination response for high-frequency endpoints. No total count.
Pass `nextCursor` as `?after=<token>` on the next request.

```json
{
  "items": [...],
  "hasMore": true,
  "nextCursor": "MTY5NTAwMDAwMDAwMDpmZmZmZmZmZi0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDA"
}
```

**Query parameters:**

| Param | Default | Max | Description |
|---|---|---|---|
| `after` | — | — | Opaque cursor from previous response |
| `limit` | `20` | `100` | Items per page |
| `useCursor` | `false` | — | Force cursor mode (omit `after` for first page) |

### ApiError

All error responses use this shape.

```json
{
  "code": "NODE_NOT_FOUND",
  "message": "Node a3b4c5d6-... not found"
}
```

---

## Nodes

Nodes are the worker processes that execute tasks. They register themselves
via gRPC; this REST API is the operator/management interface.

### `GET /api/nodes`

List all nodes, optionally filtered by status.

**Query:** `status?: "Online" | "Offline"`, `page?`, `pageSize?`

**Response `200`:** `PagedResult<NodeResponse>`

```json
{
  "items": [
    {
      "id": "a3b4c5d6-0001-0000-0000-000000000000",
      "name": "worker-eu-1",
      "status": "Online",
      "lastHeartbeatAt": "2024-11-12T09:41:02.000Z",
      "createdAt": "2024-11-01T08:00:00.000Z"
    },
    {
      "id": "a3b4c5d6-0002-0000-0000-000000000000",
      "name": "worker-us-1",
      "status": "Offline",
      "lastHeartbeatAt": "2024-11-12T08:10:00.000Z",
      "createdAt": "2024-11-01T08:05:00.000Z"
    }
  ],
  "total": 2,
  "page": 1,
  "pageSize": 20
}
```

> **Note:** Effective tags and capability lists are not yet in this response.
> They are planned as `effectiveTags: Record<string,string>` and
> `capabilities: string[]` fields in a future enrichment.

---

### `GET /api/nodes/{id}`

Get a single node by ID.

**Response `200`:** `NodeResponse`  
**Response `404`:** `NODE_NOT_FOUND`

```json
{
  "id": "a3b4c5d6-0001-0000-0000-000000000000",
  "name": "worker-eu-1",
  "status": "Online",
  "lastHeartbeatAt": "2024-11-12T09:41:02.000Z",
  "createdAt": "2024-11-01T08:00:00.000Z"
}
```

---

### `DELETE /api/nodes/{id}`

Remove a node record from the Cluster. The Node process itself is not
terminated; if it continues running it will re-register on the next heartbeat.

**Response `200`:**

```json
{ "message": "Node deleted successfully" }
```

**Response `404`:** `NODE_NOT_FOUND`

---

### `PATCH /api/nodes/{id}/tags`

Add or remove **overlay tags** on a node. Overlay tags combine with the Node's
static tags (set at registration) to form the effective tag set used for
`TaggedNodes` dispatch routing. Static tags always win on key conflict.

The updated effective tag set is returned synchronously. The Node receives the
new effective set on its next heartbeat acknowledgement.

**Request body:**

```json
{
  "add": {
    "region": "eu",
    "tier": "premium"
  },
  "remove": ["legacy"]
}
```

Both `add` and `remove` are optional. `remove` takes a list of key names.

**Response `200`:** `Record<string, string>` — the full effective tag set after
the update.

```json
{
  "region": "eu",
  "tier": "premium",
  "env": "prod"
}
```

**Response `400`:** `NODE_NOT_FOUND` (if node does not exist — routed through
`ApiError` middleware)

#### Tags in routing context

Tags are key=value pairs used to target dispatches to a subset of nodes.
When dispatching with `TaggedNodes` strategy, the `targetTags` selector must
be a **subset** of the node's effective tags for the node to be eligible.

```
effectiveTags = { region: "eu", tier: "premium", env: "prod" }
selector      = { region: "eu" }   →  match ✓
selector      = { region: "eu", tier: "basic" }  →  no match ✗
```

---

### `POST /api/nodes/{id}/env`

Queue a secret key-value pair for delivery to the Node. The Node stores it in
its local encrypted env store and uses it to resolve `{env:KEY}` placeholders
in task config. The Cluster does not persist the value after the delivery
acknowledgement window.

**Request body:**

```json
{
  "key": "DATABASE_URL",
  "value": "postgres://user:pass@host:5432/db"
}
```

**Response `202`:**

```json
{
  "opId": "f1e2d3c4-0000-0000-0000-000000000000",
  "key": "DATABASE_URL"
}
```

---

### `DELETE /api/nodes/{id}/env/{key}`

Queue a delete for a single env key on the Node.

**Response `202`:**

```json
{
  "opId": "f1e2d3c4-0001-0000-0000-000000000000",
  "key": "DATABASE_URL"
}
```

---

### `GET /api/nodes/{id}/env`

List env key names currently pending delivery to the Node. This reflects
queued-but-unacknowledged ops only; it does not reflect what the Node has
already applied.

**Response `200`:** `string[]`

```json
["DATABASE_URL", "S3_SECRET_KEY"]
```

---

## Tasks

Task definitions are reusable templates for work. A definition specifies the
task type, its default configuration payload, retry policy, and default
dispatch routing.

### `GET /api/tasks`

List all task definitions, newest first.

**Query:** `page?`, `pageSize?`

**Response `200`:** `PagedResult<TaskDefinitionResponse>`

```json
{
  "items": [
    {
      "id": "b1c2d3e4-0001-0000-0000-000000000000",
      "name": "Send Report Email",
      "description": "Renders and sends the daily report",
      "taskType": "email@1",
      "configJson": "{\"template\":\"daily-report\",\"to\":\"{param:recipient}\"}",
      "defaultStrategy": "TaggedNodes",
      "defaultTargetTagsJson": "{\"role\":\"mailer\"}",
      "version": 3,
      "maxRetries": 2,
      "retryDelaySeconds": 60,
      "retryBackoff": "Exponential",
      "createdAt": "2024-10-15T12:00:00.000Z",
      "updatedAt": "2024-11-10T14:22:00.000Z"
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 20
}
```

---

### `GET /api/tasks/{id}`

Get a single task definition.

**Response `200`:** `TaskDefinitionResponse`  
**Response `404`:** `TASK_NOT_FOUND`

---

### `POST /api/tasks`

Create a new task definition.

**Request body:**

```json
{
  "name": "Ingest CSV",
  "description": "Reads a CSV file and writes rows to the warehouse",
  "taskType": "csv-ingest@1",
  "configJson": "{\"source\":\"{param:file_url}\",\"table\":\"{param:target_table}\"}",
  "defaultStrategy": "TaggedNodes",
  "defaultTargetTags": {
    "role": "ingestor",
    "region": "eu"
  },
  "maxRetries": 3,
  "retryDelaySeconds": 30,
  "retryBackoff": "Exponential"
}
```

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `name` | string | ✓ | — | Human display name |
| `description` | string | — | — | Optional |
| `taskType` | string | — | `"default@1"` | `name@version` format |
| `configJson` | string | — | `"{}"` | JSON string; supports `{param:key}` and `{env:KEY}` placeholders |
| `defaultStrategy` | string | — | `"SpecificNode"` | `SpecificNode` \| `AnyOnlineNode` \| `AllOnlineNodes` \| `TaggedNodes` |
| `defaultTargetTags` | object | — | — | Required when `defaultStrategy` is `TaggedNodes` |
| `maxRetries` | int | — | `0` | 0 disables retries |
| `retryDelaySeconds` | int | — | `60` | Base delay in seconds |
| `retryBackoff` | string | — | `"Fixed"` | `Fixed` \| `Linear` \| `Exponential` |

**Response `201`:** `TaskDefinitionResponse`

---

### `DELETE /api/tasks/{id}`

Delete a task definition. Existing instances are preserved (soft reference via
their `configJsonSnapshot`). Pipeline steps that reference this definition
cannot be deleted if the FK constraint blocks it (behavior: 409/constraint
error from Postgres).

**Response `204`:** No content  
**Response `404`:** `TASK_NOT_FOUND`

---

### Config Placeholders

`configJson` supports two placeholder forms that are resolved at the Node
before execution:

| Placeholder | Resolved from | Example |
|---|---|---|
| `{param:KEY}` | Per-dispatch `runtimeParams` | `{param:recipient}` |
| `{env:KEY}` | Node's local encrypted env store | `{env:DATABASE_URL}` |

`runtimeParams` are passed at dispatch time (or via a schedule's
`runtimeParamsJson`). `env` keys are delivered to the Node via
`POST /api/nodes/{id}/env`.

---

## Dispatch

Dispatching creates a TaskInstance and routes it to one or more Nodes via
RabbitMQ.

### `POST /api/tasks/{id}/dispatch`

Dispatch a single task instance. All fields are optional; when omitted, the
definition's `defaultStrategy` and `defaultTargetTags` apply.

**Request body:**

```json
{
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
  "strategy": "SpecificNode",
  "targetTags": null,
  "runtimeParams": {
    "recipient": "ops@example.com",
    "report_date": "2024-11-12"
  }
}
```

| Field | Type | Notes |
|---|---|---|
| `nodeId` | uuid | Required when `strategy` is `SpecificNode` |
| `strategy` | string | Overrides the definition's `defaultStrategy` |
| `targetTags` | object | Required/used when `strategy` is `TaggedNodes` |
| `runtimeParams` | object | Arbitrary JSON; resolves `{param:KEY}` in configJson |

**Response `200`:** `TaskInstanceResponse`

```json
{
  "id": "c1d2e3f4-0001-0000-0000-000000000000",
  "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
  "status": "Dispatched",
  "resultJson": null,
  "errorMessage": null,
  "createdAt": "2024-11-12T09:42:00.000Z",
  "dispatchedAt": "2024-11-12T09:42:00.050Z",
  "completedAt": null
}
```

> When `strategy` is `AnyOnlineNode`, `nodeId` in the response is `null` until a
> Node claims the message and the claim is acknowledged.

**Error responses:**

| Code | Meaning |
|---|---|
| `TASK_NOT_FOUND` | Task definition does not exist |
| `NO_ELIGIBLE_NODES` | No online nodes match the routing criteria |
| `NODE_NOT_FOUND` | `SpecificNode` strategy but the named node doesn't exist |
| `NODE_OFFLINE` | Named node exists but is not online |
| `SPECIFIC_NODE_REQUIRES_NODE_ID` | Strategy is `SpecificNode` but no `nodeId` provided |

---

### `POST /api/tasks/{id}/dispatch-all`

Dispatch to every currently online Node. Creates one TaskInstance per node.
Useful for fleet-wide config pushes or broadcast operations.

No request body required.

**Response `200`:** `TaskInstanceResponse[]` — one entry per node dispatched to.

```json
[
  {
    "id": "c1d2e3f4-0002-0000-0000-000000000000",
    "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
    "status": "Dispatched",
    ...
  },
  {
    "id": "c1d2e3f4-0003-0000-0000-000000000000",
    "nodeId": "a3b4c5d6-0002-0000-0000-000000000000",
    "status": "Dispatched",
    ...
  }
]
```

---

### `GET /api/tasks/{id}/instances`

List instances for a task definition. Supports both offset and cursor
pagination.

**Offset mode** (default):

```
GET /api/tasks/{id}/instances?page=1&pageSize=20
```

Response: `PagedResult<TaskInstanceResponse>` with total count.

**Cursor mode** (for stable deep paging):

```
GET /api/tasks/{id}/instances?useCursor=true&limit=50
GET /api/tasks/{id}/instances?after=<nextCursor>&limit=50
```

Response: `CursorPagedResult<TaskInstanceResponse>` — no total count,
ordered `(createdAt DESC, id DESC)`.

```json
{
  "items": [...],
  "hasMore": true,
  "nextCursor": "MTY5NTAwMDAwMDAwMDpmZmZmZmZmZi0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDA"
}
```

---

### `GET /api/tasks/instances/{instanceId}`

Get a single task instance by ID.

**Response `200`:** `TaskInstanceResponse`  
**Response `404`:** `INSTANCE_NOT_FOUND`

---

### Instance Status Lifecycle

```
Pending → Dispatched → Running → Completed
                     ↘           ↗
                      Failed → Pending  (retry path, up to maxRetries)
```

| Status | Meaning |
|---|---|
| `Pending` | Created but not yet sent to a Node (or re-queued for retry) |
| `Claimed` | Node claimed from shared queue (transitional, rare) |
| `Dispatched` | Sent to Node via RabbitMQ |
| `Running` | Node has started execution |
| `Completed` | Node reported success |
| `Failed` | Node reported failure; may transition back to `Pending` if retries remain |

---

## Pipelines

Pipelines are DAGs of steps. Each step maps to a TaskDefinition and specifies
how it should be dispatched and what to do on failure.

### `GET /api/pipelines`

List all pipelines.

**Query:** `page?`, `pageSize?`

**Response `200`:** `PagedResult<PipelineResponse>`

```json
{
  "items": [
    {
      "id": "d1e2f3a4-0001-0000-0000-000000000000",
      "name": "nightly-etl",
      "description": "Extracts, transforms, and loads overnight data",
      "version": 2,
      "createdAt": "2024-10-20T00:00:00.000Z",
      "updatedAt": "2024-11-05T10:00:00.000Z",
      "steps": [
        {
          "id": "e1f2a3b4-0001-0000-0000-000000000000",
          "name": "extract",
          "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
          "dependsOn": [],
          "strategyOverride": null,
          "targetNodeId": null,
          "targetTagsJson": null,
          "failurePolicy": "FailPipeline",
          "order": 0
        },
        {
          "id": "e1f2a3b4-0002-0000-0000-000000000000",
          "name": "transform",
          "taskDefinitionId": "b1c2d3e4-0002-0000-0000-000000000000",
          "dependsOn": ["e1f2a3b4-0001-0000-0000-000000000000"],
          "strategyOverride": "TaggedNodes",
          "targetNodeId": null,
          "targetTagsJson": "{\"role\":\"transformer\"}",
          "failurePolicy": "FailPipeline",
          "order": 1
        },
        {
          "id": "e1f2a3b4-0003-0000-0000-000000000000",
          "name": "load",
          "taskDefinitionId": "b1c2d3e4-0003-0000-0000-000000000000",
          "dependsOn": ["e1f2a3b4-0002-0000-0000-000000000000"],
          "strategyOverride": null,
          "targetNodeId": null,
          "targetTagsJson": null,
          "failurePolicy": "ContinuePipeline",
          "order": 2
        }
      ]
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 20
}
```

---

### `GET /api/pipelines/{id}`

Get a single pipeline with its steps.

**Response `200`:** `PipelineResponse`  
**Response `404`:** `PIPELINE_NOT_FOUND`

---

### `POST /api/pipelines`

Create a new pipeline.

**Request body:**

```json
{
  "name": "nightly-etl",
  "description": "Extracts, transforms, and loads overnight data",
  "steps": [
    {
      "name": "extract",
      "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
      "dependsOn": [],
      "strategy": null,
      "targetNodeId": null,
      "targetTags": null,
      "failurePolicy": "FailPipeline",
      "order": 0
    },
    {
      "name": "transform",
      "taskDefinitionId": "b1c2d3e4-0002-0000-0000-000000000000",
      "dependsOn": ["extract"],
      "strategy": "TaggedNodes",
      "targetTags": { "role": "transformer" },
      "failurePolicy": "FailPipeline",
      "order": 1
    },
    {
      "name": "load",
      "taskDefinitionId": "b1c2d3e4-0003-0000-0000-000000000000",
      "dependsOn": ["transform"],
      "failurePolicy": "ContinuePipeline",
      "order": 2
    }
  ]
}
```

**Step fields:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | ✓ | Unique within pipeline; used in `dependsOn` references |
| `taskDefinitionId` | uuid | ✓ | Must exist |
| `dependsOn` | string[] | — | Step names (not IDs) in the same pipeline |
| `strategy` | string | — | Overrides the task's `defaultStrategy` |
| `targetNodeId` | uuid | — | Used with `SpecificNode` strategy |
| `targetTags` | object | — | Used with `TaggedNodes` strategy |
| `failurePolicy` | string | — | `FailPipeline` (default) \| `ContinuePipeline` |
| `order` | int | — | Display order hint |

**Response `201`:** `PipelineResponse`

**Error responses:**

| Code | Meaning |
|---|---|
| `PIPELINE_CYCLE` | `dependsOn` references form a cycle |
| `STEP_NOT_FOUND` | A `dependsOn` name does not match any step in the pipeline |
| `DUPLICATE_STEP_NAME` | Two steps share the same name |

---

### `DELETE /api/pipelines/{id}`

Delete a pipeline and all its steps. Active runs are not interrupted but
cannot be queried after the pipeline is deleted.

**Response `204`:** No content  
**Response `404`:** `PIPELINE_NOT_FOUND`

---

### Step Failure Policies

| Policy | Effect |
|---|---|
| `FailPipeline` | Step failure marks the run as Failed; downstream steps are Skipped |
| `ContinuePipeline` | Step failure is recorded but does not halt the run; disjoint branches continue |

A run is marked `Completed` when all steps have reached a terminal state
(Completed, Failed, or Skipped), even if some steps failed under
`ContinuePipeline`.

---

## Pipeline Runs

A **PipelineRun** is one execution of a pipeline. It snapshots the pipeline's
step graph at start time and orchestrates step dispatch.

### `POST /api/pipelines/{id}/run`

Start a manual pipeline run.

**Request body:** (optional)

```json
{
  "runtimeParams": {
    "date": "2024-11-12",
    "mode": "full"
  },
  "triggerReason": "manual/ops-dashboard"
}
```

`runtimeParams` is forwarded to every step's task dispatch, resolving
`{param:KEY}` placeholders in task config. `triggerReason` is a free-form
label visible in the run record (scheduled runs use `"schedule:{scheduleId}"`).

**Response `202`:** `PipelineRunResponse`

```json
{
  "id": "f1a2b3c4-0001-0000-0000-000000000000",
  "pipelineId": "d1e2f3a4-0001-0000-0000-000000000000",
  "pipelineVersion": 2,
  "status": "Running",
  "triggerReason": "manual/ops-dashboard",
  "startedAt": "2024-11-12T10:00:00.000Z",
  "completedAt": null,
  "errorMessage": null
}
```

---

### `GET /api/pipelines/runs/{runId}`

Get a single run.

**Response `200`:** `PipelineRunResponse`  
**Response `404`:** `RUN_NOT_FOUND`

---

### `GET /api/pipelines/{id}/runs`

List runs for a pipeline, newest first. Uses cursor pagination.

```
GET /api/pipelines/{id}/runs?limit=20
GET /api/pipelines/{id}/runs?after=<nextCursor>&limit=20
```

**Response `200`:** `CursorPagedResult<PipelineRunResponse>`

---

### `GET /api/pipelines/runs/{runId}/steps`

List all step instances for a run. Returns the full execution detail of each
step including its linked `TaskInstance`.

**Response `200`:** `PipelineStepInstanceResponse[]`

```json
[
  {
    "id": "a1b2c3d4-0001-0000-0000-000000000000",
    "pipelineRunId": "f1a2b3c4-0001-0000-0000-000000000000",
    "pipelineStepId": "e1f2a3b4-0001-0000-0000-000000000000",
    "taskInstanceId": "c1d2e3f4-0001-0000-0000-000000000000",
    "status": "Completed",
    "createdAt": "2024-11-12T10:00:00.000Z",
    "dispatchedAt": "2024-11-12T10:00:00.100Z",
    "completedAt": "2024-11-12T10:00:45.320Z",
    "errorMessage": null
  },
  {
    "id": "a1b2c3d4-0002-0000-0000-000000000000",
    "pipelineRunId": "f1a2b3c4-0001-0000-0000-000000000000",
    "pipelineStepId": "e1f2a3b4-0002-0000-0000-000000000000",
    "taskInstanceId": null,
    "status": "Waiting",
    "createdAt": "2024-11-12T10:00:00.000Z",
    "dispatchedAt": null,
    "completedAt": null,
    "errorMessage": null
  }
]
```

**Step instance statuses:**

| Status | Meaning |
|---|---|
| `Waiting` | Dependencies not yet complete |
| `Dispatched` | Task sent to Node |
| `Completed` | Task completed successfully |
| `Failed` | Task failed |
| `Skipped` | Upstream step failed under `FailPipeline` policy |

---

### Run Status Lifecycle

```
Running → Completed   (all steps terminal, none failed under FailPipeline)
        → Failed      (at least one step failed under FailPipeline)
        → Cancelled   (not yet implemented; reserved)
```

---

## Schedules

Schedules attach a cron trigger to a pipeline. The Cluster sweeps enabled
schedules every 10 seconds and fires runs whose `nextFireAt` ≤ now.

### `POST /api/pipelines/{id}/schedules`

Create a schedule for a pipeline.

**Request body:**

```json
{
  "cronExpression": "0 2 * * *",
  "timeZoneId": "Europe/Berlin",
  "enabled": true,
  "runtimeParams": {
    "mode": "incremental"
  }
}
```

| Field | Type | Required | Default | Notes |
|---|---|---|---|---|
| `cronExpression` | string | ✓ | — | 5-field or 6-field (with seconds) cron |
| `timeZoneId` | string | — | `"UTC"` | IANA timezone name |
| `enabled` | bool | — | `true` | `false` to create a paused schedule |
| `runtimeParams` | object | — | — | Forwarded to every run triggered by this schedule |

**5-field format** (standard): `MINUTE HOUR DOM MONTH DOW`  
**6-field format** (with seconds): `SECOND MINUTE HOUR DOM MONTH DOW`

Common cron examples:

| Expression | Meaning |
|---|---|
| `0 2 * * *` | Daily at 02:00 |
| `0 */6 * * *` | Every 6 hours |
| `30 8 * * 1-5` | Weekdays at 08:30 |
| `0 0 1 * *` | 1st of every month at midnight |
| `0 0 9 * * 1` | Every Monday at 09:00 (6-field) |

**Response `201`:** `ScheduleResponse`

```json
{
  "id": "g1h2i3j4-0001-0000-0000-000000000000",
  "pipelineId": "d1e2f3a4-0001-0000-0000-000000000000",
  "cronExpression": "0 2 * * *",
  "timeZoneId": "Europe/Berlin",
  "enabled": true,
  "lastFiredAt": null,
  "nextFireAt": "2024-11-13T01:00:00.000Z",
  "runtimeParamsJson": "{\"mode\":\"incremental\"}",
  "createdAt": "2024-11-12T10:05:00.000Z",
  "updatedAt": "2024-11-12T10:05:00.000Z"
}
```

**Error responses:**

| Code | Meaning |
|---|---|
| `PIPELINE_NOT_FOUND` | Pipeline does not exist |
| `CRON_EMPTY` | `cronExpression` is blank |
| `CRON_INVALID_FORMAT` | Wrong number of fields (not 5 or 6) |
| `CRON_INVALID` | Expression parses but is semantically invalid |
| `TIMEZONE_UNKNOWN` | IANA timezone ID not recognized |
| `TIMEZONE_INVALID` | Timezone resolved but is invalid for use |

---

### `GET /api/pipelines/{id}/schedules`

List schedules for a pipeline.

**Response `200`:** `ScheduleResponse[]`

---

### `GET /api/pipelines/schedules/{scheduleId}`

Get a single schedule.

**Response `200`:** `ScheduleResponse`  
**Response `404`:** `SCHEDULE_NOT_FOUND`

---

### `PATCH /api/pipelines/schedules/{scheduleId}`

Update a schedule. All fields are optional. Changing `cronExpression` or
`timeZoneId` recomputes `nextFireAt`. Setting `enabled: false` nulls
`nextFireAt` (pauses the schedule). Re-enabling recomputes from now.

**Request body:**

```json
{
  "cronExpression": "0 3 * * *",
  "timeZoneId": null,
  "enabled": true,
  "runtimeParams": { "mode": "full" }
}
```

**Response `200`:** `ScheduleResponse`  
**Response `404`:** `SCHEDULE_NOT_FOUND`  
**Error codes:** same as `POST /schedules`

---

### `DELETE /api/pipelines/schedules/{scheduleId}`

Delete a schedule. The pipeline itself is unaffected; future runs will not
be triggered by this schedule.

**Response `204`:** No content  
**Response `404`:** `SCHEDULE_NOT_FOUND`

---

### Schedule Sweep Internals

The `SchedulerService` runs every `Scheduling:PollIntervalSeconds` (default
10s). Each sweep:

1. Selects up to 100 schedules where `Enabled = true` AND `NextFireAt ≤ now`,
   ordered by `NextFireAt ASC`.
2. For each, calls `PipelineService.StartRunAsync` with the schedule's
   `RuntimeParamsJson` and `triggerReason: "schedule:{id}"`.
3. Advances `LastFiredAt = now` and recomputes `NextFireAt`.
4. If `StartRunAsync` throws (e.g. pipeline deleted), `NextFireAt` is not
   advanced — the schedule will retry on the next sweep.

---

## Logs

Node processes emit structured Serilog logs. These are routed via RabbitMQ to
the Cluster's `LogConsumerService`, which buffers the most recent ~1000 entries
per node and fans them out to SSE subscribers.

### `GET /api/logs/stream/{nodeId}`

Stream logs for a specific node using Server-Sent Events (SSE).

**How to connect:**

```ts
const source = new EventSource("/api/logs/stream/{nodeId}");
source.onmessage = (event) => {
  const log = JSON.parse(event.data);
  console.log(log);
};
```

Use a relative URL (not `http://localhost:5001/...`) in browser environments
using a Vite proxy — the gRPC port (5000) and REST port (5001) are separate
listeners.

**On connect:** the server replays all buffered logs for the node immediately
(so the panel is not blank on first load), then delivers new logs as they
arrive.

**Event data shape:**

```json
{
  "id": "h1i2j3k4-0001-0000-0000-000000000000",
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
  "level": "Information",
  "message": "Task started: Ingest CSV on node worker-eu-1",
  "timestamp": "2024-11-12T10:00:01.123Z",
  "exception": null
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | uuid | Log entry ID |
| `nodeId` | uuid | Originating node |
| `level` | string | `Debug` \| `Information` \| `Warning` \| `Error` \| `Critical` |
| `message` | string | Rendered log message (falls back to `messageTemplate` if rendering fails) |
| `timestamp` | string | UTC timestamp from the Node |
| `exception` | string \| null | Stack trace if an exception was attached |

**Initial ping event:** The server sends `: connected\n\n` immediately on
connection so `EventSource.onopen` fires without waiting for the first log.

---

### `GET /api/logs/buffer-status/{nodeId}`

Get the current size of the in-memory log buffer for a node. Useful for
diagnostics.

**Response `200`:**

```json
{
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
  "bufferSize": 342
}
```

---

## Health

### `GET /health`

Cluster liveness check. Does not require authentication.

**Response `200`:**

```json
{ "status": "ok" }
```

---

## Cross-Feature Workflows

### 1. Register a node and dispatch a task to it

```
1. Node starts up and self-registers via gRPC (automatic; not a REST call)

2. GET /api/nodes
   → Find the node ID once it appears

3. POST /api/tasks
   Body: { name: "ping", taskType: "default@1", configJson: "{}" }
   → Get taskId

4. POST /api/tasks/{taskId}/dispatch
   Body: { nodeId: "<nodeId>", strategy: "SpecificNode" }
   → Get instanceId, status = "Dispatched"

5. GET /api/tasks/instances/{instanceId}
   → Poll until status = "Completed" or "Failed"
```

---

### 2. Tag a node and dispatch using tag selector

```
1. PATCH /api/nodes/{nodeId}/tags
   Body: { add: { role: "ingestor", region: "eu" } }
   → Effective tags updated

2. POST /api/tasks
   Body: {
     name: "EU Ingest",
     taskType: "csv-ingest@1",
     defaultStrategy: "TaggedNodes",
     defaultTargetTags: { role: "ingestor" }
   }
   → Get taskId

3. POST /api/tasks/{taskId}/dispatch
   Body: {}   ← uses definition defaults; strategy = TaggedNodes, selector = { role: "ingestor" }
   → Instance dispatched to first eligible tagged node

   Or override at dispatch time:
   Body: { strategy: "TaggedNodes", targetTags: { role: "ingestor", region: "eu" } }
   → Only nodes with BOTH tags are eligible
```

---

### 3. Build and run a multi-step pipeline

```
1. POST /api/tasks ×3   (create extract, transform, load task definitions)
   → extractId, transformId, loadId

2. POST /api/pipelines
   Body: {
     name: "nightly-etl",
     steps: [
       { name: "extract", taskDefinitionId: extractId, dependsOn: [] },
       { name: "transform", taskDefinitionId: transformId, dependsOn: ["extract"] },
       { name: "load", taskDefinitionId: loadId, dependsOn: ["transform"],
         failurePolicy: "ContinuePipeline" }
     ]
   }
   → pipelineId

3. POST /api/pipelines/{pipelineId}/run
   Body: { runtimeParams: { date: "2024-11-12" } }
   → runId, status = "Running"

4. GET /api/pipelines/runs/{runId}/steps
   → Monitor step-by-step progress

5. GET /api/pipelines/runs/{runId}
   → Poll until status = "Completed" or "Failed"

6. On failure: GET /api/tasks/instances/{stepInstance.taskInstanceId}
   → errorMessage shows why the step failed
```

---

### 4. Schedule a pipeline to run nightly

```
1. (Pipeline already created: pipelineId known)

2. POST /api/pipelines/{pipelineId}/schedules
   Body: {
     cronExpression: "0 2 * * *",
     timeZoneId: "Europe/London",
     runtimeParams: { mode: "incremental" }
   }
   → scheduleId, nextFireAt shows next scheduled UTC time

3. GET /api/pipelines/{pipelineId}/schedules
   → Verify schedule is listed with enabled=true

4. Cluster fires automatically at nextFireAt. Each run has:
   triggerReason = "schedule:{scheduleId}"

5. To pause without deleting:
   PATCH /api/pipelines/schedules/{scheduleId}
   Body: { enabled: false }
   → nextFireAt = null

6. To resume:
   PATCH /api/pipelines/schedules/{scheduleId}
   Body: { enabled: true }
   → nextFireAt recomputed from now
```

---

### 5. Pass secrets to a node for task execution

```
1. POST /api/nodes/{nodeId}/env
   Body: { key: "DATABASE_URL", value: "postgres://user:pass@host/db" }
   → opId returned; value queued for delivery on next heartbeat

2. Create a task that uses the secret:
   POST /api/tasks
   Body: {
     name: "DB Export",
     taskType: "db-export@1",
     configJson: "{\"connection\":\"{env:DATABASE_URL}\",\"table\":\"{param:table}\"}"
   }

3. POST /api/tasks/{taskId}/dispatch
   Body: {
     nodeId: "<nodeId>",
     runtimeParams: { table: "orders" }
   }
   → Node resolves {env:DATABASE_URL} from its local store
     and {param:table} from runtimeParams before executing
```

---

### 6. Observe a running pipeline via logs

```
1. Run is underway: runId known

2. GET /api/pipelines/runs/{runId}/steps
   → Find the dispatched step; get taskInstanceId and nodeId from
     GET /api/tasks/instances/{taskInstanceId}

3. Open SSE stream:
   const es = new EventSource("/api/logs/stream/{nodeId}");
   → Receive buffered + live logs from that node

4. Filter client-side by level or message to focus on the relevant task.
   Log entries do not carry instanceId — correlate by timestamp range and
   nodeId against the instance's dispatchedAt/completedAt window.

5. On failure: instance.errorMessage has the top-level failure reason;
   the SSE log stream will have the full stack trace if the Node attached it.
```

---

## Error Reference

All error responses return HTTP 4xx/5xx with body `{ "code": string, "message": string }`.

| Code | HTTP | Feature | Meaning |
|---|---|---|---|
| `NODE_NOT_FOUND` | 404 | Nodes | Node ID does not exist |
| `NODE_OFFLINE` | 400 | Dispatch | Named node is not online |
| `NO_ELIGIBLE_NODES` | 400 | Dispatch | No online node matches the routing criteria |
| `SPECIFIC_NODE_REQUIRES_NODE_ID` | 400 | Dispatch | `SpecificNode` strategy with no `nodeId` |
| `TASK_NOT_FOUND` | 404 | Tasks | Task definition ID does not exist |
| `INSTANCE_NOT_FOUND` | 404 | Tasks | Task instance ID does not exist |
| `INVALID_CURSOR` | 400 | Pagination | `after` cursor token is malformed |
| `PIPELINE_NOT_FOUND` | 404 | Pipelines | Pipeline ID does not exist |
| `PIPELINE_CYCLE` | 400 | Pipelines | Step `dependsOn` graph contains a cycle |
| `STEP_NOT_FOUND` | 400 | Pipelines | A `dependsOn` step name does not exist in the pipeline |
| `DUPLICATE_STEP_NAME` | 400 | Pipelines | Two steps share the same name |
| `RUN_NOT_FOUND` | 404 | Runs | Pipeline run ID does not exist |
| `SCHEDULE_NOT_FOUND` | 404 | Schedules | Schedule ID does not exist |
| `CRON_EMPTY` | 400 | Schedules | `cronExpression` is blank or whitespace |
| `CRON_INVALID_FORMAT` | 400 | Schedules | Expression is not 5 or 6 fields |
| `CRON_INVALID` | 400 | Schedules | Expression is syntactically invalid |
| `TIMEZONE_UNKNOWN` | 400 | Schedules | IANA timezone ID not recognized |
| `TIMEZONE_INVALID` | 400 | Schedules | Timezone resolved but invalid |

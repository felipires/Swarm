---
title: Pipeline Runs
---

# Pipeline Runs

## Start a run

```
POST /api/pipelines/{id}/run
```

**Request body** (optional):

```json
{
  "runtimeParams": {
    "date": "2024-11-12",
    "mode": "full"
  },
  "triggerReason": "manual/ops-dashboard"
}
```

`runtimeParams` is forwarded to every step's task dispatch. `triggerReason` is a free-form label (scheduled runs use `"schedule:{scheduleId}"`).

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

## Get a run

```
GET /api/pipelines/runs/{runId}
```

**Response `200`:** `PipelineRunResponse`  
**Response `404`:** `RUN_NOT_FOUND`

---

## List runs for a pipeline

```
GET /api/pipelines/{id}/runs
```

Cursor-paginated, newest first.

```bash
GET /api/pipelines/{id}/runs?limit=20
GET /api/pipelines/{id}/runs?after=<nextCursor>&limit=20
```

**Response `200`:** `CursorPagedResult<PipelineRunResponse>`

---

## Get step instances for a run

```
GET /api/pipelines/runs/{runId}/steps
```

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

## Run lifecycle

```
Running → Completed   all steps terminal, none failed under FailPipeline
        → Failed      at least one FailPipeline step failed
```

A run is `Completed` when all steps are in a terminal state (including Failed under `ContinuePipeline`).

---

## Example workflow

```bash
# Start a run
RUN=$(curl -s -X POST http://localhost:5001/api/pipelines/<pipelineId>/run \
  -H "Content-Type: application/json" \
  -d '{"runtimeParams": {"date": "2024-11-12"}}' | jq -r '.id')

# Poll status
curl http://localhost:5001/api/pipelines/runs/$RUN

# Inspect step progress
curl http://localhost:5001/api/pipelines/runs/$RUN/steps
```

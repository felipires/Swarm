---
title: Error Reference
---

# Error Reference

All API errors return a consistent JSON envelope:

```json
{
  "error": "NODE_NOT_FOUND",
  "message": "Node a3b4c5d6-0001-0000-0000-000000000000 was not found."
}
```

The `error` field is a stable machine-readable code. The `message` field is human-readable and may change between versions — don't parse it.

---

## Node errors

| Code | HTTP | Feature | Meaning |
|---|---|---|---|
| `NODE_NOT_FOUND` | 404 | Nodes | No node exists with the given ID |
| `NODE_OFFLINE` | 409 | Dispatch | The targeted node has not sent a heartbeat recently and is considered offline |
| `NO_ELIGIBLE_NODES` | 409 | Dispatch | No online nodes match the dispatch strategy (tag filters, available capacity, etc.) |
| `SPECIFIC_NODE_REQUIRES_NODE_ID` | 400 | Dispatch | `strategy: SpecificNode` was specified without a `nodeId` |

---

## Task errors

| Code | HTTP | Feature | Meaning |
|---|---|---|---|
| `TASK_NOT_FOUND` | 404 | Tasks | No task definition exists with the given ID |
| `INSTANCE_NOT_FOUND` | 404 | Task instances | No task instance exists with the given ID |
| `INVALID_CURSOR` | 400 | Task instances | The `after` cursor value is malformed or expired |

---

## Pipeline errors

| Code | HTTP | Feature | Meaning |
|---|---|---|---|
| `PIPELINE_NOT_FOUND` | 404 | Pipelines | No pipeline exists with the given ID |
| `PIPELINE_CYCLE` | 422 | Pipeline create/update | The `dependsOn` graph contains a cycle |
| `STEP_NOT_FOUND` | 422 | Pipeline create/update | A `dependsOn` name references a step name that doesn't exist |
| `DUPLICATE_STEP_NAME` | 422 | Pipeline create/update | Two steps share the same name within the pipeline |
| `RUN_NOT_FOUND` | 404 | Pipeline runs | No run exists with the given ID |

---

## Schedule errors

| Code | HTTP | Feature | Meaning |
|---|---|---|---|
| `SCHEDULE_NOT_FOUND` | 404 | Schedules | No schedule exists with the given ID |
| `CRON_EMPTY` | 400 | Schedule create/update | `cronExpression` is blank or whitespace-only |
| `CRON_INVALID_FORMAT` | 400 | Schedule create/update | Expression does not have 5 or 6 space-separated fields |
| `CRON_INVALID` | 400 | Schedule create/update | Expression parses structurally but is semantically invalid (e.g. `0 99 * * *`) |
| `TIMEZONE_UNKNOWN` | 400 | Schedule create/update | The `timeZoneId` string is not a recognized IANA timezone name |
| `TIMEZONE_INVALID` | 400 | Schedule create/update | The timezone was recognized but is not valid for use in scheduling |

---

## HTTP status codes

| Status | When used |
|---|---|
| `200 OK` | Successful GET, PATCH |
| `201 Created` | Successful POST that created a resource |
| `202 Accepted` | Dispatch or pipeline run started (async) |
| `204 No Content` | Successful DELETE |
| `400 Bad Request` | Invalid input (missing required fields, malformed values) |
| `404 Not Found` | Resource does not exist |
| `409 Conflict` | Valid request but the current state prevents it (node offline, no eligible nodes) |
| `422 Unprocessable Entity` | Request is well-formed but semantically invalid (pipeline graph errors) |
| `500 Internal Server Error` | Unexpected server fault — check Cluster logs |

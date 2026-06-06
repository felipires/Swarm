---
title: List & Get Tasks
---

# Task Definitions

## List task definitions

```
GET /api/tasks
```

Returns all task definitions, newest first.

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
  "pageSize": 50
}
```

---

## Get a task definition

```
GET /api/tasks/{id}
```

**Response `200`:** `TaskDefinitionResponse`  
**Response `404`:** `TASK_NOT_FOUND`

---

## Create a task definition

```
POST /api/tasks
```

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
| `name` | string | ✓ | — | Display name |
| `description` | string | — | — | Optional |
| `taskType` | string | — | `"default@1"` | `name@version` format |
| `configJson` | string | — | `"{}"` | Supports `{param:key}` and `{env:KEY}` placeholders |
| `defaultStrategy` | string | — | `"SpecificNode"` | `SpecificNode` \| `AnyOnlineNode` \| `AllOnlineNodes` \| `TaggedNodes` |
| `defaultTargetTags` | object | — | — | Required when `defaultStrategy` is `TaggedNodes` |
| `maxRetries` | int | — | `0` | 0 disables retries |
| `retryDelaySeconds` | int | — | `60` | Base delay |
| `retryBackoff` | string | — | `"Fixed"` | `Fixed` \| `Linear` \| `Exponential` |

**Response `201`:** `TaskDefinitionResponse`

---

## Delete a task definition

```
DELETE /api/tasks/{id}
```

**Response `204`:** No content  
**Response `404`:** `TASK_NOT_FOUND`

Existing task instances are preserved. Pipeline steps referencing this definition may block deletion via FK constraint.

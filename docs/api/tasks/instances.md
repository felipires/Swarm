---
title: Task Instances
---

# Task Instances

## List instances for a task

```
GET /api/tasks/{id}/instances
```

Supports both offset and cursor pagination. The task instance list is ordered `createdAt DESC, id DESC`.

### Offset mode (default)

```bash
GET /api/tasks/{id}/instances?page=1&pageSize=50
```

**Response:** `PagedResult<TaskInstanceResponse>` with total count.

### Cursor mode

Better for stable deep paging on high-frequency tasks.

```bash
# First page
GET /api/tasks/{id}/instances?useCursor=true&limit=50

# Next page
GET /api/tasks/{id}/instances?useCursor=true&after=<nextCursor>&limit=50
```

**Response:**

```json
{
  "items": [...],
  "hasMore": true,
  "nextCursor": "MTY5NTAwMDAwMDAwMDpmZmZmZmZmZi0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDA"
}
```

A malformed `after` value returns `400 INVALID_CURSOR`.

---

## Get a single instance

```
GET /api/tasks/instances/{instanceId}
```

**Response `200`:** `TaskInstanceResponse`

```json
{
  "id": "c1d2e3f4-0001-0000-0000-000000000000",
  "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000",
  "status": "Completed",
  "taskType": "http@1",
  "configJsonSnapshot": "{\"url\":\"https://api.example.com/notify\"}",
  "runtimeParamsJson": "{\"recipient\":\"ops@example.com\"}",
  "resultJson": "{\"statusCode\":200,\"body\":\"ok\"}",
  "errorMessage": null,
  "retryCount": 1,
  "createdAt": "2024-11-12T09:42:00.000Z",
  "dispatchedAt": "2024-11-12T09:42:00.050Z",
  "completedAt": "2024-11-12T09:42:01.320Z"
}
```

**Response `404`:** `INSTANCE_NOT_FOUND`

---

## Instance status lifecycle

```
Pending ──► Dispatched ──► Running ──► Completed
                                   └──► Failed
                                         │   (retry)
                                         └──► Pending
```

| Status | Meaning |
|---|---|
| `Pending` | Created or waiting for retry |
| `Claimed` | Claimed from shared queue (transitional) |
| `Dispatched` | Published to RabbitMQ |
| `Running` | Node has started execution |
| `Completed` | Success |
| `Failed` | Failure; may retry if `retryCount < maxRetries` |

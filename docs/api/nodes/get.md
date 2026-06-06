---
title: Get Node
---

# Get Node

```
GET /api/nodes/{id}
```

Returns a single node by ID with its capabilities and latest metrics.

## Path parameters

| Param | Description |
|---|---|
| `id` | Node UUID |

## Response `200`

```json
{
  "id": "a3b4c5d6-0001-0000-0000-000000000000",
  "name": "worker-eu-1",
  "status": "Online",
  "lastHeartbeatAt": "2024-11-12T09:41:02.000Z",
  "createdAt": "2024-11-01T08:00:00.000Z",
  "cpuCores": 8,
  "totalMemoryBytes": 17179869184,
  "effectiveTags": { "region": "eu", "role": "ingestor" },
  "capabilities": ["http@1", "sql@1"],
  "latestMetrics": {
    "recordedAt": "2024-11-12T09:41:00.000Z",
    "cpuPercent": 12.4,
    "memoryUsedBytes": 134217728,
    "memoryAvailableBytes": 16911651328,
    "inFlightTasks": 2,
    "uptimeSeconds": 86412,
    "health": "Healthy"
  }
}
```

## Response `404`

```json
{ "code": "NODE_NOT_FOUND", "message": "Node a3b4c5d6-... not found" }
```

## Example

```bash
curl http://localhost:5001/api/nodes/a3b4c5d6-0001-0000-0000-000000000000
```

---
title: List Nodes
---

# List Nodes

```
GET /api/nodes
```

Returns all registered nodes, optionally filtered by status.

## Query parameters

| Param | Type | Default | Description |
|---|---|---|---|
| `status` | `"Online"` \| `"Offline"` | — | Filter by node status |
| `page` | int | 1 | Page number (1-based) |
| `pageSize` | int | 50 | Items per page (max 200) |

## Response `200`

```json
{
  "items": [
    {
      "id": "a3b4c5d6-0001-0000-0000-000000000000",
      "name": "worker-eu-1",
      "status": "Online",
      "lastHeartbeatAt": "2024-11-12T09:41:02.000Z",
      "createdAt": "2024-11-01T08:00:00.000Z",
      "cpuCores": 8,
      "totalMemoryBytes": 17179869184,
      "effectiveTags": {
        "region": "eu",
        "role": "ingestor"
      },
      "capabilities": ["http@1", "sql@1", "csv-ingest@1"],
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
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

### NodeResponse fields

| Field | Type | Description |
|---|---|---|
| `id` | uuid | Node UUID |
| `name` | string | Auto-generated display name |
| `status` | string | `"Online"` \| `"Offline"` |
| `lastHeartbeatAt` | timestamp | Last heartbeat received |
| `createdAt` | timestamp | Registration timestamp |
| `cpuCores` | int? | CPU core count (null until first registration with P5-1 Node) |
| `totalMemoryBytes` | long? | Total memory in bytes |
| `effectiveTags` | object | Merged static + overlay tags |
| `capabilities` | string[] | `name@version` task types this Node handles |
| `latestMetrics` | object? | Latest live metrics snapshot from Redis; null if unavailable |

### latestMetrics fields

| Field | Type | Description |
|---|---|---|
| `recordedAt` | timestamp | When the snapshot was recorded |
| `cpuPercent` | float | CPU usage as % of all cores |
| `memoryUsedBytes` | long | Process working set |
| `memoryAvailableBytes` | long | System-available memory |
| `inFlightTasks` | int | Tasks currently executing |
| `uptimeSeconds` | long | Seconds since Node process started |
| `health` | string | `"Healthy"` \| `"Degraded"` \| `"Unhealthy"` |

## Example

```bash
# All nodes
curl http://localhost:5001/api/nodes

# Online nodes only
curl "http://localhost:5001/api/nodes?status=Online"

# Page 2
curl "http://localhost:5001/api/nodes?page=2&pageSize=20"
```

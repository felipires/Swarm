---
title: Node Metrics
---

# Node Metrics History

```
GET /api/nodes/{id}/metrics
```

Returns the recent live-metrics history for a node (newest first, up to 120 samples). Returns an empty array if Redis is unavailable or no history has been recorded yet.

## Path parameters

| Param | Description |
|---|---|
| `id` | Node UUID |

## Response `200`

`NodeMetricsDto[]` — array of snapshots, newest first.

```json
[
  {
    "recordedAt": "2024-11-12T09:41:30.000Z",
    "cpuPercent": 14.2,
    "memoryUsedBytes": 135266304,
    "memoryAvailableBytes": 16910880768,
    "inFlightTasks": 3,
    "uptimeSeconds": 86442,
    "health": "Healthy"
  },
  {
    "recordedAt": "2024-11-12T09:41:00.000Z",
    "cpuPercent": 12.4,
    "memoryUsedBytes": 134217728,
    "memoryAvailableBytes": 16911651328,
    "inFlightTasks": 2,
    "uptimeSeconds": 86412,
    "health": "Healthy"
  }
]
```

## Response `404`

```json
{ "code": "NODE_NOT_FOUND", "message": "Node ... not found" }
```

## Storage

Metrics are stored in Redis using two keys per node:

| Key pattern | Content | Expiry |
|---|---|---|
| `node:metrics:latest:{nodeId}` | Latest snapshot | `Metrics:TtlSeconds` (default 300s) |
| `node:metrics:history:{nodeId}` | Capped list (LPUSH+LTRIM) | Same TTL |

A node that stops heartbeating will have its metrics expire automatically after the TTL. The history endpoint returns an empty array for nodes with no cached data.

## Health thresholds

| Status | CPU | Memory ratio |
|---|---|---|
| `Healthy` | < 85% | < 90% |
| `Degraded` | ≥ 85% | ≥ 90% |
| `Unhealthy` | ≥ 95% | ≥ 97% |

## Example

```bash
curl http://localhost:5001/api/nodes/<nodeId>/metrics
```

## Notes

- The latest snapshot is also embedded in `GET /api/nodes/{id}` as `latestMetrics`.
- This history endpoint is intended for trend visualization (sparklines, time-series charts).
- History length is controlled by `Metrics:HistoryLength` (default 120 samples).

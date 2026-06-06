---
title: API Overview
---

# API Reference

The Swarm Cluster exposes a REST API for operators and integrations.

## Base URL

```
http://localhost:5001/api
```

In production, replace with your Cluster host. The REST port is separate from the gRPC port (5000).

## Authentication

No authentication is currently enforced. All endpoints are accessible without credentials. See [Security →](../security).

## Common conventions

| Convention | Value |
|---|---|
| Content-Type | `application/json` for all request bodies |
| Timestamps | ISO 8601 UTC — `"2024-11-12T09:41:03.123Z"` |
| IDs | UUID v4 strings |
| Errors | `{"code": "ERROR_CODE", "message": "human-readable message"}` |

## Swagger UI

Available at `http://localhost:5001/swagger` when `ASPNETCORE_ENVIRONMENT=Development`.

## Pagination

Two modes are available:

### Offset pagination

Used on most list endpoints:

```
GET /api/nodes?page=1&pageSize=50
```

```json
{
  "items": [...],
  "total": 120,
  "page": 1,
  "pageSize": 50
}
```

| Param | Default | Max |
|---|---|---|
| `page` | 1 | — |
| `pageSize` | 50 | 200 |

### Cursor pagination

Used on high-frequency endpoints (task instances, pipeline runs):

```
GET /api/tasks/{id}/instances?useCursor=true&limit=50
GET /api/tasks/{id}/instances?useCursor=true&after=<nextCursor>&limit=50
```

```json
{
  "items": [...],
  "hasMore": true,
  "nextCursor": "MTY5NTAwMDAwMDAwMDpmZmZmZmZmZi0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDA"
}
```

Pass `nextCursor` as `?after=` on the next request. A malformed cursor returns `400 INVALID_CURSOR`.

## Error shape

```json
{
  "code": "NODE_NOT_FOUND",
  "message": "Node a3b4c5d6-... not found"
}
```

See [Error Reference →](errors) for all codes.

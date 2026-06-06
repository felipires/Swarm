---
title: Delete Node
---

# Delete Node

```
DELETE /api/nodes/{id}
```

Removes the node record from the Cluster database. The Node process itself is not terminated. If the Node continues running, it will re-register on its next heartbeat cycle.

## Path parameters

| Param | Description |
|---|---|
| `id` | Node UUID |

## Response `200`

```json
{ "message": "Node deleted successfully" }
```

## Response `404`

```json
{ "code": "NODE_NOT_FOUND", "message": "Node a3b4c5d6-... not found" }
```

## Example

```bash
curl -X DELETE http://localhost:5001/api/nodes/a3b4c5d6-0001-0000-0000-000000000000
```

## Notes

- Deleting a node does not drain its pending task queues.
- In-flight `TaskInstance` records retain the `nodeId` reference; they are not altered.
- Overlay tags and env-op records for the deleted node are removed via cascade delete.

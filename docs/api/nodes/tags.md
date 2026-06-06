---
title: Manage Tags
---

# Manage Node Tags

```
PATCH /api/nodes/{id}/tags
```

Add or remove **overlay tags** on a node. Overlay tags combine with the Node's static tags to form the effective tag set used for `TaggedNodes` dispatch routing.

**Static tags always win on key conflict.** Setting an overlay tag that matches a static tag key has no effect on the effective value.

## Path parameters

| Param | Description |
|---|---|
| `id` | Node UUID |

## Request body

All fields are optional.

```json
{
  "add": {
    "region": "eu",
    "tier": "premium"
  },
  "remove": ["legacy", "deprecated"]
}
```

| Field | Type | Description |
|---|---|---|
| `add` | object | Key-value pairs to upsert into the overlay |
| `remove` | string[] | Keys to remove from the overlay |

## Response `200`

The full effective overlay tag set (not the effective merged set) after the change.

```json
{
  "region": "eu",
  "tier": "premium"
}
```

The Node receives the updated effective tag set (static ∪ overlay) on its next heartbeat acknowledgement.

## Example

```bash
# Add tags
curl -X PATCH http://localhost:5001/api/nodes/<nodeId>/tags \
  -H "Content-Type: application/json" \
  -d '{"add": {"region": "eu", "tier": "premium"}}'

# Remove a tag
curl -X PATCH http://localhost:5001/api/nodes/<nodeId>/tags \
  -H "Content-Type: application/json" \
  -d '{"remove": ["tier"]}'
```

## Tag matching semantics

For `TaggedNodes` dispatch, the `targetTags` selector must be a **subset** of the Node's effective tags:

```
Effective: { region: "eu", role: "ingestor", env: "prod" }
Selector:  { region: "eu" }                   → match ✓
Selector:  { region: "eu", tier: "premium" }  → no match ✗
```

See [Tags →](../../concepts/tags) for the full concept reference.

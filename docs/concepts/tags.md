---
title: Tags
---

# Tags

Tags are `key=value` pairs attached to Nodes. They are the primary routing surface for the `TaggedNodes` dispatch strategy.

## Two layers

| Layer | Who sets it | How |
|---|---|---|
| **Static tags** | The Node process | Environment variables (`SWARM_TAG_<key>=<value>`) or `appsettings.json` (`Swarm:Tags`) |
| **Overlay tags** | Operators via the REST API | `PATCH /api/nodes/{id}/tags` |

**Static tags win on key conflict.** If a Node reports `region=eu` and an operator tries to set `region=us` via overlay, the effective tag remains `region=eu`.

## Effective tag set

The **effective tag set** is the union of static and overlay tags (static wins). It is:

- Denormalized into a `EffectiveTagsJson` JSONB column on the `Node` row
- GIN-indexed for fast containment queries
- Updated at the two write sites: registration (static) and `PATCH /tags` (overlay)

## Routing with tags

When dispatching with `TaggedNodes`, the `targetTags` selector must be a **subset** of a Node's effective tags:

```
Node effective tags:   { region: "eu", role: "ingestor", env: "prod" }
Selector:              { region: "eu" }                   → match ✓
Selector:              { region: "eu", role: "ingestor" } → match ✓
Selector:              { region: "us" }                   → no match ✗
Selector:              { region: "eu", tier: "premium" }  → no match ✗
```

## Setting overlay tags

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

The response is the full effective overlay tag set after the change. The Node receives the updated effective set on its next heartbeat acknowledgement.

## Tag discovery on the Node

Configure static tags via environment variables:

```bash
SWARM_TAG_region=eu
SWARM_TAG_role=ingestor
SWARM_TAG_env=prod
```

Or via `appsettings.json`:

```json
{
  "Swarm": {
    "Tags": {
      "region": "eu",
      "role": "ingestor"
    }
  }
}
```

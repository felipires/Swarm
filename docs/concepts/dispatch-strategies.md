---
title: Dispatch Strategies
---

# Dispatch Strategies

A dispatch strategy determines how a task is routed to one or more Nodes.

## Available strategies

### `SpecificNode`

Routes to a single named Node by ID.

```json
{
  "strategy": "SpecificNode",
  "nodeId": "a3b4c5d6-0001-0000-0000-000000000000"
}
```

- `nodeId` is required
- Fails with `NODE_NOT_FOUND` if the node doesn't exist
- Fails with `NODE_OFFLINE` if the node is not `Online`

### `AnyOnlineNode`

Routes to whichever Node picks up the message first from a shared RabbitMQ queue.

```json
{ "strategy": "AnyOnlineNode" }
```

- The task message goes to `tasks.shared.<taskType>` (competing consumers)
- `nodeId` is null in the response until a Node claims the message
- Use for load-balanced, stateless work

### `AllOnlineNodes`

Dispatches one instance to every currently online Node. Creates N instances atomically (single DB transaction).

```json
{ "strategy": "AllOnlineNodes" }
```

- Returns an array of `TaskInstanceResponse` — one per node
- Useful for fleet-wide config pushes, cache invalidations, broadcast operations

### `TaggedNodes`

Routes to Nodes whose **effective tag set** is a superset of the provided selector.

```json
{
  "strategy": "TaggedNodes",
  "targetTags": { "region": "eu", "role": "ingestor" }
}
```

- The selector must be a strict subset of the Node's effective tags
- Uses a GIN-indexed `@>` JSONB containment query on Postgres
- Multiple eligible Nodes: the Cluster dispatches to each matching Node via their per-node queue (similar to `AllOnlineNodes` but filtered)
- Fails with `NO_ELIGIBLE_NODES` if no Node matches

## Precedence

The dispatch strategy is resolved in this order:

1. Strategy provided in `POST /api/tasks/{id}/dispatch` body
2. Strategy set on the pipeline step (`strategyOverride`)
3. `defaultStrategy` on the `TaskDefinition`
4. If none is set: `SpecificNode` (requires explicit `nodeId` at dispatch)

## Queue topology

```
SpecificNode   → tasks.node.<nodeId>
AnyOnlineNode  → tasks.shared.<taskType>
AllOnlineNodes → tasks.node.<nodeId>  (one message per node)
TaggedNodes    → tasks.node.<nodeId>  (one message per eligible node)
               OR tasks.tagged.<hash> (shared queue for tag-matched subscribers)
```

The `tasks.tagged.<hash>` path uses a deterministic SHA-256 hash of the canonical (sorted) tag selector JSON. Nodes subscribe to the relevant `tasks.tagged.*` queues based on the subscription list returned in each heartbeat response.

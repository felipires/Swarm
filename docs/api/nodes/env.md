---
title: Node Env Secrets
---

# Node Env Secrets

The Cluster can queue encrypted secret deliveries to individual Nodes. Once delivered and applied, the Node uses these secrets to resolve `{env:KEY}` placeholders in task config.

## Queue a secret

```
POST /api/nodes/{id}/env
```

Enqueues a key-value secret for delivery to the Node. The value is delivered on the Node's next heartbeat (within `Heartbeat:IntervalSeconds` seconds) and stored encrypted (AES-256-GCM) in the Node's local SQLite store.

**Request body:**

```json
{
  "key": "DATABASE_URL",
  "value": "postgres://user:pass@host:5432/db"
}
```

**Response `202`:**

```json
{
  "opId": "f1e2d3c4-0000-0000-0000-000000000000",
  "key": "DATABASE_URL"
}
```

```bash
curl -X POST http://localhost:5001/api/nodes/<nodeId>/env \
  -H "Content-Type: application/json" \
  -d '{"key": "DATABASE_URL", "value": "postgres://user:pass@host/db"}'
```

## Delete a secret

```
DELETE /api/nodes/{id}/env/{key}
```

Queues a deletion for the named key. The Node removes it from its local store on the next heartbeat.

**Response `202`:**

```json
{
  "opId": "f1e2d3c4-0001-0000-0000-000000000000",
  "key": "DATABASE_URL"
}
```

## List pending keys

```
GET /api/nodes/{id}/env
```

Returns the list of keys currently queued for delivery (not yet acknowledged by the Node). Does not reflect keys the Node has already applied.

**Response `200`:** `string[]`

```json
["DATABASE_URL", "S3_SECRET_KEY"]
```

## Delivery flow

```
POST /api/nodes/{id}/env
        │ value held in NodeEnvOp (plaintext, delivery window)
        ▼
Node heartbeat response carries pending_env_ops
        │
Node applies ops → encrypts with AES-256-GCM → stores in SQLite
        │
Node acks ops in next heartbeat request (acked_env_op_ids)
        │
Cluster deletes the NodeEnvOp rows
```

The plaintext is only at risk during the **delivery window** (one heartbeat interval). After acknowledgement, the Cluster holds no plaintext copy.

## Security note

Set `SWARM_NODE_MACHINE_KEY` on each Node so encrypted secrets survive container rebuilds. Without it, a random key is generated per process start and secrets from before the rebuild cannot be decrypted.

See [Security →](../../security) for the broader trust model.

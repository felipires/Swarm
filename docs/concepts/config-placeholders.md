---
title: Config Placeholders & Value Resolution
---

# Config Placeholders & Value Resolution

Task `configJson` supports a placeholder syntax that is resolved at the Node immediately before handler invocation. This lets task definitions be reusable templates with values filled in at dispatch or stored securely on the Node.

## Placeholder syntax

```
{tier:KEY}
{tier:KEY:modifier}
{tier:KEY:modifier=value}
```

## Tiers

| Tier | Syntax | Source |
|---|---|---|
| 1 — Task env | `{env:KEY}` | `SWARM_TASKENV_<KEY>` environment variable on the Node |
| 2 — Encrypted store | `{env:KEY}` | Node's local AES-256-GCM encrypted SQLite store |
| 3 — Runtime params | `{param:KEY}` | Per-dispatch `runtimeParams` |
| 4 — Config | `{config:KEY}` | Static `configJson` values (self-referential, rare) |

Tier 1 and 2 share the `{env:KEY}` syntax — Tier 1 (env var) is checked first.

## Modifiers

| Modifier | Effect |
|---|---|
| `:required` | Throws if the value is absent or empty |
| `:secret` | Value is tracked; redacted in structured logs |
| `:default=<value>` | Fallback if the value is absent |
| `:type=string` | (default) No coercion |
| `:type=int` | Parse as integer |
| `:type=float` | Parse as float |
| `:type=bool` | Parse as boolean |
| `:type=json` | Inject as raw JSON (not a quoted string) |

## Examples

```json
{
  "connection": "{env:DATABASE_URL:required:secret}",
  "table": "{param:target_table:default=orders}",
  "batch_size": "{param:batch:type=int:default=100}",
  "dry_run": "{param:dry_run:type=bool:default=false}"
}
```

## Env secrets via the API

Push a secret to a Node:

```bash
curl -X POST http://localhost:5001/api/nodes/<nodeId>/env \
  -H "Content-Type: application/json" \
  -d '{"key": "DATABASE_URL", "value": "postgres://user:pass@host/db"}'
```

The value is queued and delivered to the Node on its next heartbeat. The Node encrypts it locally using AES-256-GCM and stores it in SQLite. The Cluster does not retain the plaintext after the delivery acknowledgement window.

## Node env variables (Tier 1)

Set directly in the Node process environment:

```bash
SWARM_TASKENV_DATABASE_URL=postgres://user:pass@host/db
```

These are resolved first and take precedence over the encrypted store.

## Security note

Values marked `:secret` are tracked by the `SecretRedactionEnricher` and replaced with `[REDACTED]` in any structured log entries the handler emits. This only applies to **parameterized** log calls — string-interpolated messages (`$"token={secret}"`) bake the value into the message template and cannot be rewritten.

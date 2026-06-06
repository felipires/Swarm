---
title: Task Config Examples
---

# Task Config Examples

## HTTP handler (`http@1`)

```json
{
  "name": "Webhook Trigger",
  "taskType": "http@1",
  "configJson": "{\"url\":\"https://api.example.com/process\",\"method\":\"POST\",\"headers\":{\"Authorization\":\"Bearer {env:API_TOKEN:secret}\"},\"body\":\"{\\\"id\\\":\\\"{param:record_id}\\\"}\",\"timeoutSeconds\":30}",
  "maxRetries": 3,
  "retryDelaySeconds": 60,
  "retryBackoff": "Exponential"
}
```

Config fields for `http@1`:

| Field | Required | Description |
|---|---|---|
| `url` | ✓ | Target URL |
| `method` | — | `GET`, `POST`, `PUT`, `PATCH`, `DELETE` (default: `POST`) |
| `headers` | — | Key-value pairs |
| `body` | — | Request body string |
| `timeoutSeconds` | — | Request timeout (default: 30) |

---

## SQL handler (`sql@1`)

```json
{
  "name": "Export Orders",
  "taskType": "sql@1",
  "configJson": "{\"provider\":\"postgres\",\"connectionString\":\"{env:DB_URL:required:secret}\",\"query\":\"SELECT * FROM orders WHERE date = '{param:date}'\"}",
  "defaultStrategy": "TaggedNodes",
  "defaultTargetTags": { "role": "db-worker" }
}
```

Config fields for `sql@1`:

| Field | Required | Description |
|---|---|---|
| `provider` | — | `postgres` (default), `mssql`, `mysql` |
| `connectionString` | ✓ | Use `{env:KEY}` for credentials |
| `query` | ✓ | SQL query string |

---

## Webhook handler (`webhook@1`)

```json
{
  "name": "Signed Notify",
  "taskType": "webhook@1",
  "configJson": "{\"url\":\"https://hooks.example.com/swarm\",\"secret\":\"{env:WEBHOOK_SECRET:secret}\",\"payload\":\"{\\\"event\\\":\\\"{param:event_type}\\\"}\"}",
  "maxRetries": 2,
  "retryDelaySeconds": 30
}
```

Config fields for `webhook@1`:

| Field | Required | Description |
|---|---|---|
| `url` | ✓ | Target URL |
| `secret` | ✓ | HMAC-SHA256 signing secret |
| `payload` | — | Request body |

The handler signs the payload with `HMAC-SHA256(secret, body)` and sends it as the `X-Swarm-Signature` header.

---
title: Retry Policy
---

# Retry Policy

Failed task instances can automatically retry based on settings on the `TaskDefinition`.

## Configuration fields

| Field | Default | Description |
|---|---|---|
| `maxRetries` | `0` | Maximum number of retry attempts (0 disables retries) |
| `retryDelaySeconds` | `60` | Base delay in seconds |
| `retryBackoff` | `Fixed` | Backoff strategy: `Fixed`, `Linear`, or `Exponential` |

## Backoff strategies

| Strategy | Delay formula | Example (base=60s) |
|---|---|---|
| `Fixed` | `retryDelaySeconds` | 60s, 60s, 60s |
| `Linear` | `retryDelaySeconds × attempt` | 60s, 120s, 180s |
| `Exponential` | `retryDelaySeconds × 2^(attempt-1)` | 60s, 120s, 240s |

Maximum delay is capped at **86400 seconds (24h)** regardless of backoff.

## Retry flow

```
TaskInstance fails (Node reports failure)
        │
TaskResultConsumerService evaluates:
  retryCount < maxRetries?
        │
   Yes  │
        ▼
  status = Pending
  retryCount++
  retryAfter = now + computed delay
        │
RetrySchedulerService polls (every 30s)
  WHERE status = Pending AND retryAfter <= now
        │
        ▼
  Re-dispatches instance via fresh outbox row
  status = Dispatched
```

## Creating a task with retry

```bash
curl -X POST http://localhost:5001/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Resilient HTTP Call",
    "taskType": "http@1",
    "configJson": "{\"url\": \"https://api.example.com/process\", \"method\": \"POST\"}",
    "maxRetries": 3,
    "retryDelaySeconds": 30,
    "retryBackoff": "Exponential"
  }'
```

With this config, failures will retry at:
- Attempt 1: 30s delay
- Attempt 2: 60s delay
- Attempt 3: 120s delay
- Attempt 4 (final): permanent `Failed`

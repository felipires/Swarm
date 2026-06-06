---
title: Schedules
---

# Schedules

Schedules attach a cron expression to a pipeline. The Cluster fires runs automatically when `nextFireAt` passes.

## Create a schedule

```
POST /api/pipelines/{id}/schedules
```

**Request body:**

```json
{
  "cronExpression": "0 2 * * *",
  "timeZoneId": "Europe/Berlin",
  "enabled": true,
  "runtimeParams": {
    "mode": "incremental"
  }
}
```

| Field | Required | Default | Notes |
|---|---|---|---|
| `cronExpression` | ✓ | — | 5-field or 6-field cron |
| `timeZoneId` | — | `"UTC"` | IANA timezone name |
| `enabled` | — | `true` | `false` to create a paused schedule |
| `runtimeParams` | — | — | Forwarded to every run triggered by this schedule |

**Cron formats:**

| Format | Fields | Example |
|---|---|---|
| 5-field (standard) | `MIN HOUR DOM MONTH DOW` | `0 2 * * *` = daily at 02:00 |
| 6-field (with seconds) | `SEC MIN HOUR DOM MONTH DOW` | `0 0 9 * * 1` = Mondays at 09:00 |

**Common examples:**

| Expression | Meaning |
|---|---|
| `0 2 * * *` | Daily at 02:00 |
| `0 */6 * * *` | Every 6 hours |
| `30 8 * * 1-5` | Weekdays at 08:30 |
| `0 0 1 * *` | 1st of every month at midnight |

**Response `201`:** `ScheduleResponse`

```json
{
  "id": "g1h2i3j4-0001-0000-0000-000000000000",
  "pipelineId": "d1e2f3a4-0001-0000-0000-000000000000",
  "cronExpression": "0 2 * * *",
  "timeZoneId": "Europe/Berlin",
  "enabled": true,
  "lastFiredAt": null,
  "nextFireAt": "2024-11-13T01:00:00.000Z",
  "runtimeParamsJson": "{\"mode\":\"incremental\"}",
  "createdAt": "2024-11-12T10:05:00.000Z",
  "updatedAt": "2024-11-12T10:05:00.000Z"
}
```

**Error codes:**

| Code | Meaning |
|---|---|
| `PIPELINE_NOT_FOUND` | Pipeline does not exist |
| `CRON_EMPTY` | `cronExpression` is blank |
| `CRON_INVALID_FORMAT` | Not 5 or 6 fields |
| `CRON_INVALID` | Expression parses but is semantically invalid |
| `TIMEZONE_UNKNOWN` | IANA timezone ID not recognized |
| `TIMEZONE_INVALID` | Timezone resolved but invalid for use |

---

## List schedules for a pipeline

```
GET /api/pipelines/{id}/schedules
```

**Response `200`:** `ScheduleResponse[]`

---

## Get a schedule

```
GET /api/pipelines/schedules/{scheduleId}
```

**Response `200`:** `ScheduleResponse`  
**Response `404`:** `SCHEDULE_NOT_FOUND`

---

## Update a schedule

```
PATCH /api/pipelines/schedules/{scheduleId}
```

All fields are optional. Changing `cronExpression` or `timeZoneId` recomputes `nextFireAt`. Setting `enabled: false` pauses the schedule (nulls `nextFireAt`).

```json
{
  "cronExpression": "0 3 * * *",
  "enabled": true,
  "runtimeParams": { "mode": "full" }
}
```

**Response `200`:** `ScheduleResponse`  
**Response `404`:** `SCHEDULE_NOT_FOUND`

---

## Delete a schedule

```
DELETE /api/pipelines/schedules/{scheduleId}
```

**Response `204`:** No content  
**Response `404`:** `SCHEDULE_NOT_FOUND`

---

## Sweep internals

The `SchedulerService` polls every `Scheduling:PollIntervalSeconds` (default 10s):

1. Selects up to 100 schedules where `Enabled = true` AND `NextFireAt ≤ now`
2. For each, calls `PipelineService.StartRunAsync` with the schedule's `RuntimeParamsJson`
3. Advances `LastFiredAt` and recomputes `NextFireAt`
4. If the pipeline trigger throws, `NextFireAt` is **not** advanced — the schedule retries on the next sweep

---

## Example

```bash
# Create a nightly schedule
curl -X POST http://localhost:5001/api/pipelines/<pipelineId>/schedules \
  -H "Content-Type: application/json" \
  -d '{
    "cronExpression": "0 2 * * *",
    "timeZoneId": "America/Sao_Paulo",
    "runtimeParams": {"mode": "incremental"}
  }'

# Pause it
curl -X PATCH http://localhost:5001/api/pipelines/schedules/<scheduleId> \
  -H "Content-Type: application/json" \
  -d '{"enabled": false}'

# Resume it
curl -X PATCH http://localhost:5001/api/pipelines/schedules/<scheduleId> \
  -H "Content-Type: application/json" \
  -d '{"enabled": true}'
```

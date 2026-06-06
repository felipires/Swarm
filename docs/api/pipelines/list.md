---
title: List & Get Pipelines
---

# Pipelines

## List pipelines

```
GET /api/pipelines
```

**Query:** `page?`, `pageSize?`

**Response `200`:** `PagedResult<PipelineResponse>`

```json
{
  "items": [
    {
      "id": "d1e2f3a4-0001-0000-0000-000000000000",
      "name": "nightly-etl",
      "description": "Extracts, transforms, and loads overnight data",
      "version": 2,
      "createdAt": "2024-10-20T00:00:00.000Z",
      "updatedAt": "2024-11-05T10:00:00.000Z",
      "steps": [
        {
          "id": "e1f2a3b4-0001-0000-0000-000000000000",
          "name": "extract",
          "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
          "dependsOn": [],
          "strategyOverride": null,
          "targetNodeId": null,
          "targetTagsJson": null,
          "failurePolicy": "FailPipeline",
          "order": 0
        },
        {
          "id": "e1f2a3b4-0002-0000-0000-000000000000",
          "name": "transform",
          "taskDefinitionId": "b1c2d3e4-0002-0000-0000-000000000000",
          "dependsOn": ["e1f2a3b4-0001-0000-0000-000000000000"],
          "strategyOverride": "TaggedNodes",
          "targetTagsJson": "{\"role\":\"transformer\"}",
          "failurePolicy": "FailPipeline",
          "order": 1
        }
      ]
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

---

## Get a pipeline

```
GET /api/pipelines/{id}
```

**Response `200`:** `PipelineResponse`  
**Response `404`:** `PIPELINE_NOT_FOUND`

---

## Create a pipeline

```
POST /api/pipelines
```

**Request body:**

```json
{
  "name": "nightly-etl",
  "description": "Extracts, transforms, and loads overnight data",
  "steps": [
    {
      "name": "extract",
      "taskDefinitionId": "b1c2d3e4-0001-0000-0000-000000000000",
      "dependsOn": [],
      "failurePolicy": "FailPipeline",
      "order": 0
    },
    {
      "name": "transform",
      "taskDefinitionId": "b1c2d3e4-0002-0000-0000-000000000000",
      "dependsOn": ["extract"],
      "strategy": "TaggedNodes",
      "targetTags": { "role": "transformer" },
      "failurePolicy": "FailPipeline",
      "order": 1
    },
    {
      "name": "load",
      "taskDefinitionId": "b1c2d3e4-0003-0000-0000-000000000000",
      "dependsOn": ["transform"],
      "failurePolicy": "ContinuePipeline",
      "order": 2
    }
  ]
}
```

**Step fields:**

| Field | Required | Notes |
|---|---|---|
| `name` | ✓ | Unique within pipeline; used in `dependsOn` references |
| `taskDefinitionId` | ✓ | Must exist |
| `dependsOn` | — | Step names (not IDs) |
| `strategy` | — | Overrides the task's `defaultStrategy` |
| `targetNodeId` | — | Used with `SpecificNode` |
| `targetTags` | — | Used with `TaggedNodes` |
| `failurePolicy` | — | `FailPipeline` (default) \| `ContinuePipeline` |
| `order` | — | Display order hint |

**Response `201`:** `PipelineResponse`

**Error codes:**

| Code | Meaning |
|---|---|
| `PIPELINE_CYCLE` | `dependsOn` references form a cycle |
| `STEP_NOT_FOUND` | A `dependsOn` name doesn't match any step |
| `DUPLICATE_STEP_NAME` | Two steps share the same name |

---

## Delete a pipeline

```
DELETE /api/pipelines/{id}
```

**Response `204`:** No content  
**Response `404`:** `PIPELINE_NOT_FOUND`

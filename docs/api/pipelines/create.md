---
title: Create & Delete Pipeline
---

# Create a pipeline

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
| `name` | ✓ | Unique within the pipeline; used in `dependsOn` references |
| `taskDefinitionId` | ✓ | Must refer to an existing task definition |
| `dependsOn` | — | Step **names** (not IDs) this step waits on |
| `strategy` | — | Overrides the task's `defaultStrategy` for this step |
| `targetNodeId` | — | Used with `SpecificNode` strategy |
| `targetTags` | — | Used with `TaggedNodes` strategy |
| `failurePolicy` | — | `FailPipeline` (default) \| `ContinuePipeline` |
| `order` | — | Display order hint; does not affect execution order |

**Response `201`:** `PipelineResponse`

**Error codes:**

| Code | Meaning |
|---|---|
| `PIPELINE_CYCLE` | `dependsOn` references form a cycle |
| `STEP_NOT_FOUND` | A `dependsOn` name doesn't match any step |
| `DUPLICATE_STEP_NAME` | Two steps share the same name |

---

# Delete a pipeline

```
DELETE /api/pipelines/{id}
```

**Response `204`:** No content  
**Response `404`:** `PIPELINE_NOT_FOUND`

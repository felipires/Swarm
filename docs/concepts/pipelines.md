---
title: Pipelines
---

# Pipelines

A Pipeline is a directed acyclic graph (DAG) of steps. Each step maps to a `TaskDefinition` and declares which other steps it depends on.

## Step graph

Steps reference each other by **name** in `dependsOn`. The Cluster validates the graph at creation time:

- No cycles
- No self-references
- No dangling references (a `dependsOn` name that doesn't match any step)
- No duplicate step names within the pipeline

```json
{
  "name": "nightly-etl",
  "steps": [
    { "name": "extract",   "taskDefinitionId": "...", "dependsOn": [] },
    { "name": "transform", "taskDefinitionId": "...", "dependsOn": ["extract"] },
    { "name": "load",      "taskDefinitionId": "...", "dependsOn": ["transform"] }
  ]
}
```

## Pipeline runs

`POST /api/pipelines/{id}/run` starts an execution. The run:

1. **Snapshots** the step graph at start time. Subsequent definition edits don't affect the in-flight run.
2. **Dispatches** steps with no unsatisfied dependencies immediately.
3. **Advances** after each step completes — the `InProcessStepAdvancer` evaluates newly-unblocked steps and dispatches them.
4. **Resolves** when all steps reach a terminal state (Completed, Failed, or Skipped).

## Failure policies

Each step has an independent `failurePolicy`:

| Policy | Effect on failure |
|---|---|
| `FailPipeline` (default) | Step failure marks the run `Failed`; all transitive downstream steps are `Skipped` |
| `ContinuePipeline` | Step failure is recorded but execution continues; disjoint branches are unaffected |

A run is `Completed` when all steps are terminal, even if some steps failed under `ContinuePipeline`.

## Step dispatch overrides

Steps can override the task definition's default routing:

```json
{
  "name": "transform",
  "strategyOverride": "TaggedNodes",
  "targetTags": { "role": "transformer", "region": "eu" }
}
```

## Runtime parameters

`runtimeParams` passed to `POST .../run` are forwarded to every step's task dispatch, resolving `{param:KEY}` placeholders in each step's task config.

## Versioning

Every pipeline edit increments `version`. A `PipelineRun` records the `pipelineVersion` at start. The step snapshot (`stepsSnapshotJson`) makes the run immune to later definition changes.

---
title: Database Migrations
---

# Database Migrations

Swarm uses EF Core 8 migrations to manage the Postgres schema.

## Running migrations

```bash
dotnet ef database update --project src/Swarm.Cluster
```

This applies all pending migrations in order. It is idempotent — running it twice has no effect.

## Migration history

Migrations are applied in the order listed here. All must be applied in sequence on a fresh database.

| Migration | Description |
|---|---|
| `AddTaskTypeToTaskDefinition` | Adds `TaskType` to task definitions |
| `AddPendingDispatchOutbox` | Outbox table for reliable dispatch |
| `P2_5_NodeTagSystem` | Static tags, overlay tags, effective tag column |
| `P0_3b_NodeCapability` | Node capability declarations |
| `P0_3a_HybridQueueTopology` | Shared queue tracking |
| `P1_6_RuntimeParamsJson` | Per-dispatch runtime parameters |
| `P1_5a_NodeEnvOps` | Env-secret operation queue |
| `P0_3a_TaggedRoutes` | Tagged route hash table |
| `P1_4_P1_2_SnapshotAndRetry` | Config snapshot + retry fields |
| `P1_1_Pipelines` | Pipeline DAG entities |
| `P1_3_Schedules` | Cron schedule table |
| `P3_3_NodeEffectiveTags` | GIN-indexed JSONB effective tags |
| `P3_3a_TaggedRouteLastUsedIndex` | Index on `LastUsedAt` for retention |
| `P5_1_NodeCapacity` | `CpuCores` and `TotalMemoryBytes` on the Node row |

## Adding a migration

After modifying a model in `src/Swarm.Cluster/Models/`:

```bash
dotnet ef migrations add <MigrationName> --project src/Swarm.Cluster
```

Review the generated file in `src/Swarm.Cluster/Migrations/` before applying.

```bash
dotnet ef database update --project src/Swarm.Cluster
```

## Rolling back

Roll back to a named migration:

```bash
dotnet ef database update <PreviousMigrationName> --project src/Swarm.Cluster
```

Roll back to empty (removes all tables):

```bash
dotnet ef database update 0 --project src/Swarm.Cluster
```

## Design-time context

The project includes a `DesignTimeDbContextFactory` that reads the connection string from `appsettings.json` (and `appsettings.Development.json`) when running EF CLI commands outside of the host. This is intentional — it is not a security concern for a local-dev config file.

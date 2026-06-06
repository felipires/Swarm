---
title: Introduction
slug: /
---

# Swarm

Swarm is a **distributed task and pipeline orchestrator** built on .NET 8. It runs as a **Cluster** (control plane) paired with any number of **Nodes** (workers). The Cluster exposes a REST API for operators and an internal gRPC channel for Nodes; Nodes receive work through RabbitMQ and report results back.

## What it does

| Feature | Description |
|---|---|
| **Task dispatch** | Route work to specific nodes, any available node, all nodes, or tag-matched subsets |
| **Pipelines** | Run DAGs of tasks with dependency tracking, failure policies, and step-level observability |
| **Scheduling** | Attach cron expressions (5- or 6-field) to pipelines; the Cluster fires them automatically |
| **Retry policy** | Fixed, linear, or exponential backoff per task definition |
| **Node tagging** | Static tags (Node-reported) + operator overlay tags; GIN-indexed for fast routing |
| **Env secrets** | Encrypted per-node secret store; secrets delivered via heartbeat and resolved in task config |
| **Live metrics** | CPU, memory, in-flight tasks, and health status per node — stored in Redis with TTL |
| **Log streaming** | Server-Sent Events stream of structured Node logs to the operator dashboard |

## Architecture at a glance

```
Operator / Dashboard
       │  REST :5001
       ▼
┌─────────────────────────────────┐
│           Cluster               │
│  ASP.NET Core · EF Core · gRPC  │
│  Postgres · Redis · RabbitMQ    │
└───────────┬─────────┬───────────┘
  gRPC :5000│         │ RabbitMQ
     ┌──────┘         └──────┐
     ▼                       ▼
┌─────────┐           ┌─────────┐
│  Node A │           │  Node B │
│ .NET SDK │           │ .NET SDK │
└─────────┘           └─────────┘
```

## Quick links

- **[Quickstart →](getting-started/quickstart)** — running in 5 minutes with Docker Compose
- **[API Reference →](api/overview)** — complete endpoint documentation
- **[Writing handlers →](sdk/writing-handlers)** — building custom task types
- **[Security →](security)** — current trust model and known limitations

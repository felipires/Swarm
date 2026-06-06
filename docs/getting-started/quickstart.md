---
title: Quickstart
sidebar_label: Quickstart
---

# Quickstart

Get the full Swarm stack running locally in under 5 minutes.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker Engine + Compose v2)
- Git

## 1. Clone the repo

```bash
git clone https://github.com/your-org/swarm.git
cd swarm
```

## 2. Start the stack

```bash
docker compose -f scripts/docker-compose.yml up --build
```

This brings up:

| Service | Port | Description |
|---|---|---|
| Postgres | 5432 | Cluster database |
| Redis | 6379 | Node metrics store |
| RabbitMQ | 5672 / 15672 | Message broker (management UI at :15672) |
| Cluster | 5000 (gRPC), 5001 (REST) | Control plane |
| Node | — | One worker process |
| Frontend | 5173 | Ops dashboard |

First run downloads images and builds .NET projects — expect ~2 minutes.

## 3. Apply migrations

If this is a fresh database, run migrations before or shortly after starting:

```bash
dotnet ef database update --project src/Swarm.Cluster
```

Or let the Compose stack apply them automatically if you have `DOTNET_RUNNING_IN_CONTAINER` handling configured.

## 4. Verify

```bash
curl http://localhost:5001/health
# → {"status":"ok"}

curl http://localhost:5001/api/nodes
# → {"items":[...],"total":1,"page":1,"pageSize":50}
```

Open the dashboard at `http://localhost:5173`.

## 5. Dispatch your first task

```bash
# Create a task definition
curl -s -X POST http://localhost:5001/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"name":"Hello","taskType":"default@1","configJson":"{}"}' \
  | jq '{id:.id}'

# Dispatch it (replace <taskId> and <nodeId> with the IDs above)
curl -s -X POST http://localhost:5001/api/tasks/<taskId>/dispatch \
  -H "Content-Type: application/json" \
  -d '{"strategy":"AnyOnlineNode"}'
```

The instance status will move from `Dispatched` → `Running` → `Completed` within seconds.

## Next steps

- [Docker Compose reference →](docker-compose) — environment variables, secrets, scaling
- [From source →](from-source) — running without Docker
- [Writing handlers →](../sdk/writing-handlers) — building custom task types

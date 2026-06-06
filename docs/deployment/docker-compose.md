---
title: Docker Compose Deployment
---

# Docker Compose Deployment

The `scripts/docker-compose.yml` file defines the full Swarm stack. It is the recommended way to run Swarm for development and small single-host deployments.

## Services

| Service | Image | Ports |
|---|---|---|
| `postgres` | `postgres:16-alpine` | 5432 |
| `redis` | `redis:7-alpine` | 6379 |
| `rabbitmq` | `rabbitmq:3.12-management-alpine` | 5672, 15672 |
| `cluster` | Built from `src/Swarm.Cluster` | 5000, 5001 |
| `node` | Built from `src/Swarm.Node` | — |
| `frontend` | Built from `src/frontend` | 5173 |

## Starting the stack

```bash
docker compose -f scripts/docker-compose.yml up --build
```

## Environment overrides

Create `scripts/.env`:

```dotenv
POSTGRES_USER=postgres
POSTGRES_PASSWORD=change-me
POSTGRES_DB=swarm

RABBITMQ_USER=swarm
RABBITMQ_PASSWORD=change-me

NODE_API_KEY=my-production-node-key
```

## Persistent volumes

The compose file mounts named volumes for Postgres and the Node database. Do not use `down -v` in production — it destroys all data.

```bash
# Safe stop (preserves volumes)
docker compose -f scripts/docker-compose.yml down

# Destructive (wipes database — dev reset only)
docker compose -f scripts/docker-compose.yml down -v
```

## Scaling nodes

```bash
docker compose -f scripts/docker-compose.yml up --scale node=4
```

Each node container gets its own SQLite database (from its volume mount) and registers as an independent node. Set `SWARM_NODE_MACHINE_KEY` consistently across all nodes if they need to share env secrets from the same operator delivery.

## Applying migrations inside compose

```bash
docker compose -f scripts/docker-compose.yml run --rm cluster \
  dotnet ef database update
```

Or add a one-shot migration service to the compose file that runs before the cluster service starts.

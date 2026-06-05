# Getting started

Swarm is a standalone orchestration platform: a **Cluster** (control plane) and a
fleet of **Nodes** (workers) run as independent services. This page covers local
setup and the configuration each component needs.

## Run the stack with Docker Compose

```bash
docker compose -f scripts/docker-compose.yml up --build
```

This brings up Postgres, Redis, RabbitMQ, one Cluster, one Node, and the frontend
on a shared network. Defaults are fine for a local trial; override secrets via a
`.env` file next to the compose file (it is gitignored) — see the variables below.

> **Security note.** Swarm has no enforced authentication yet (decision D7; see
> [trust-model.md](trust-model.md)). Run it on a trusted network / single-operator
> setup only until the P4 security phase lands.

## Configuration model

There are two configuration planes — keep them separate:

| Plane | What | Where it lives |
|-------|------|----------------|
| **System config** | Infra & deployment: DB connection, RabbitMQ creds, gRPC URLs, Node `ApiKey`, heartbeat intervals | `appsettings.json` + environment variables |
| **Task config** | Handler runtime values: API tokens, customer DB strings, runtime params | `TaskDefinition.ConfigJson`, Node env store (`SWARM_TASKENV_*`) |

`appsettings.json` ships **local-dev defaults only**. Every secret value must be
overridden in production via environment variables. Environment variables always
win over `appsettings.json`. For local development you can instead copy
`appsettings.Development.json.example` → `appsettings.Development.json` (gitignored)
and fill in values there.

## Required / overridable environment variables

Config keys are supplied as env vars using the ASP.NET `__` (double-underscore)
separator. These are the keys the code actually reads.

### Cluster (`src/Swarm.Cluster`)

| Variable | Default (dev) | Notes |
|----------|---------------|-------|
| `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=swarm;Username=postgres;Password=postgres` | Postgres. **Override in prod.** |
| `RabbitMQ__Hostname` | `localhost` | Broker host |
| `RabbitMQ__Port` | `5672` | |
| `RabbitMQ__Username` | `guest` | **Override in prod.** |
| `RabbitMQ__Password` | `guest` | **Override in prod.** |
| `RabbitMQ__VirtualHost` | `/` | |
| `Redis__Connection` | `redis:6379` | |
| `Heartbeat__TimeoutSeconds` | `300` | Node considered offline after this |
| `Retry__PollIntervalSeconds` | `30` | Retry scheduler poll cadence |
| `Scheduling__PollIntervalSeconds` | `10` | Cron scheduler poll cadence |
| `Logging__RetentionDays` | `30` | Log retention; `<= 0` disables |
| `ASPNETCORE_URLS` | — | Kestrel bind (compose sets `http://+:5000`) |

### Node (`src/Swarm.Node`)

| Variable | Default (dev) | Notes |
|----------|---------------|-------|
| `ApiKey` | *(blank in committed config)* | Identifies the Node to the Cluster. **Must be supplied** (env or dev config). |
| `ClusterUrl` | `http://localhost:5000` | Cluster gRPC endpoint |
| `RabbitMQ__Hostname` | `localhost` | Pre-registration fallback; the Cluster returns broker creds at registration |
| `RabbitMQ__Username` / `RabbitMQ__Password` | `guest` / `guest` | Pre-registration fallback |
| `Heartbeat__IntervalSeconds` | `30` | Heartbeat cadence |
| `SWARM_NODE_MACHINE_KEY` | *(random, persisted to `.machinekey`)* | Derives the AES key for the encrypted env-secret store. Set it to keep secrets decryptable across container rebuilds — back it up. |

### Node task-config and tagging (set per Node, task plane)

| Pattern | Purpose |
|---------|---------|
| `SWARM_TASKENV_<KEY>` | Tier-1 task env values resolved via `{env:<KEY>}` placeholders |
| `SWARM_TAG_<key>=<value>` | Static Node tags for capability routing (D6) |

### Compose-level overrides (`.env` beside docker-compose.yml)

| Variable | Default | Feeds |
|----------|---------|-------|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | `postgres` / `postgres` / `swarm` | Postgres service + Cluster connection string |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | `guest` / `guest` | RabbitMQ service + Cluster broker creds |
| `NODE_API_KEY` | `node-dev-key` | Node `ApiKey` |

## Running from source

```bash
dotnet run --project src/Swarm.Cluster      # control plane (gRPC :5000, REST :5001, HTTPS :5002)
dotnet run --project src/Swarm.Node         # a worker
```

Apply Cluster migrations against your Postgres before first run:

```bash
dotnet ef database update --project src/Swarm.Cluster
```

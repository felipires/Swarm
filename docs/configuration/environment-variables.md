---
title: Environment Variables
---

# Environment Variables

All configuration can be supplied via environment variables. ASP.NET Core uses `__` (double underscore) as the section separator.

## Cluster

| Variable | Default | Notes |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=swarm;Username=postgres;Password=postgres` | Postgres connection string. Override in production. |
| `RabbitMQ__Hostname` | `localhost` | RabbitMQ host |
| `RabbitMQ__Port` | `5672` | RabbitMQ AMQP port |
| `RabbitMQ__Username` | `guest` | Override in production |
| `RabbitMQ__Password` | `guest` | Override in production |
| `RabbitMQ__VirtualHost` | `/` | RabbitMQ vhost |
| `Redis__Connection` | `localhost:6379` | StackExchange.Redis connection string |
| `Heartbeat__TimeoutSeconds` | `300` | Node considered offline after this many seconds without a heartbeat |
| `Retry__PollIntervalSeconds` | `30` | How often the retry scheduler polls for due retries |
| `Scheduling__PollIntervalSeconds` | `10` | How often the cron scheduler polls for due schedules |
| `Metrics__TtlSeconds` | `300` | Redis TTL for node metrics snapshots |
| `Metrics__HistoryLength` | `120` | Number of metric samples retained in Redis per node |
| `Logging__RetentionDays` | `30` | Delete log entries older than this. Set to `0` to disable. |
| `TaggedRoutes__RetentionDays` | `30` | Delete tagged routes unused for this many days. |
| `ASPNETCORE_ENVIRONMENT` | — | Set to `Development` to enable Swagger UI |
| `ASPNETCORE_URLS` | — | Kestrel bind override (compose sets `http://+:5001`) |

## Node

| Variable | Default | Notes |
|---|---|---|
| `ApiKey` | *(blank)* | Sent to the Cluster at registration. Must be supplied. |
| `ClusterUrl` | `http://localhost:5000` | Cluster gRPC endpoint |
| `RabbitMQ__Hostname` | `localhost` | Pre-registration fallback; overwritten by Cluster at registration |
| `RabbitMQ__Username` | `guest` | Pre-registration fallback |
| `RabbitMQ__Password` | `guest` | Pre-registration fallback |
| `Heartbeat__IntervalSeconds` | `30` | Heartbeat cadence |
| `SWARM_NODE_MACHINE_KEY` | *(random, persisted)* | AES key derivation input for the encrypted env-secret store. Set for persistence across rebuilds. |
| `ASPNETCORE_ENVIRONMENT` | — | Set to `Development` for verbose logging |

## Node task environment (Tier 1 resolution)

| Pattern | Purpose |
|---|---|
| `SWARM_TASKENV_<KEY>` | Injected into task config via `{env:KEY}` placeholder (Tier 1) |
| `SWARM_TAG_<key>=<value>` | Static Node tags used for `TaggedNodes` dispatch routing |

## Docker Compose `.env` variables

These are consumed by `scripts/docker-compose.yml`:

| Variable | Default | Feeds |
|---|---|---|
| `POSTGRES_USER` | `postgres` | Postgres service + Cluster connection |
| `POSTGRES_PASSWORD` | `postgres` | Postgres service + Cluster connection |
| `POSTGRES_DB` | `swarm` | Postgres service + Cluster connection |
| `RABBITMQ_USER` | `guest` | RabbitMQ service + Cluster config |
| `RABBITMQ_PASSWORD` | `guest` | RabbitMQ service + Cluster config |
| `NODE_API_KEY` | `node-dev-key` | Node `ApiKey` |

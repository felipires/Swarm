---
title: Requirements
---

# Requirements

## Runtime dependencies

| Dependency | Version | Role |
|---|---|---|
| **Postgres** | 14+ | Cluster state store (tasks, nodes, pipelines, schedules) |
| **RabbitMQ** | 3.12+ | Task/result/claim/log message broker |
| **Redis** | 7+ | Node live-metrics store (optional — metrics degrade gracefully if unreachable) |
| **.NET Runtime** | 8.0+ | Required to run Cluster and Node from source |

## Build dependencies

| Dependency | Version | Role |
|---|---|---|
| **.NET SDK** | 8.0+ | Build Cluster and Node |
| **Node.js** | 20+ | Build frontend and docs site |
| **npm** | 10+ | Frontend package management |
| **dotnet-ef** | 8.0+ | Database migrations (`dotnet tool install -g dotnet-ef`) |

## Ports used

| Port | Protocol | Service |
|---|---|---|
| 5000 | HTTP/2 (h2c) | Cluster gRPC — Node registration and heartbeats |
| 5001 | HTTP/1.1 | Cluster REST API |
| 5002 | HTTPS | Cluster REST API (TLS) |
| 5173 | HTTP/1.1 | Frontend dev server |
| 5432 | TCP | Postgres |
| 5672 | AMQP | RabbitMQ |
| 6379 | TCP | Redis |
| 15672 | HTTP | RabbitMQ management UI |

## Hardware sizing (development)

The defaults are tuned for a single developer laptop:

- Cluster: ~150 MB RSS idle
- Node: ~80 MB RSS idle
- Redis memory usage: proportional to the number of active nodes × heartbeat history (120 samples/node × ~500 bytes/sample)

For production sizing, see [Deployment → Production](../deployment/production).

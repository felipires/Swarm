---
title: Cluster Configuration
---

# Cluster Configuration

The Cluster reads configuration from `appsettings.json`, `appsettings.{Environment}.json`, and environment variables. Environment variables always win.

## appsettings.json structure

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=swarm;Username=postgres;Password=postgres"
  },
  "RabbitMQ": {
    "Hostname": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest",
    "VirtualHost": "/"
  },
  "Redis": {
    "Connection": "localhost:6379"
  },
  "Heartbeat": {
    "TimeoutSeconds": 300
  },
  "Retry": {
    "PollIntervalSeconds": 30
  },
  "Scheduling": {
    "PollIntervalSeconds": 10
  },
  "Metrics": {
    "TtlSeconds": 300,
    "HistoryLength": 120
  },
  "Logging": {
    "RetentionDays": 30,
    "RetentionCheckIntervalHours": 6
  },
  "TaggedRoutes": {
    "RetentionDays": 30,
    "RetentionCheckIntervalHours": 6
  }
}
```

## Development override

Copy the example and fill in your values:

```bash
cp src/Swarm.Cluster/appsettings.Development.json.example \
   src/Swarm.Cluster/appsettings.Development.json
```

This file is gitignored.

## Configuration planes

Swarm separates two planes of configuration:

| Plane | Examples | Where |
|---|---|---|
| **System config** | Postgres creds, RabbitMQ creds, Redis, gRPC URLs | `appsettings.json` + env vars |
| **Task config** | API tokens, DB connection strings used inside tasks | `TaskDefinition.configJson`, Node env store |

System-plane credentials in `appsettings.json` are intentional (local-dev defaults). They must be overridden via environment variables in production.

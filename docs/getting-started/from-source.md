---
title: From Source
---

# Running from Source

Running without Docker requires Postgres, RabbitMQ, and Redis to be available separately.

## 1. Install dependencies

```bash
# .NET SDK 8+
# https://dotnet.microsoft.com/download

# EF Core CLI
dotnet tool install -g dotnet-ef
```

## 2. Configure the Cluster

Copy and edit the dev config:

```bash
cp src/Swarm.Cluster/appsettings.Development.json.example \
   src/Swarm.Cluster/appsettings.Development.json
```

Edit the connection strings to point at your local Postgres, RabbitMQ, and Redis instances.

## 3. Apply database migrations

```bash
dotnet ef database update --project src/Swarm.Cluster
```

This creates all tables in the configured Postgres database. The migration history is stored in the `__EFMigrationsHistory` table.

## 4. Start the Cluster

```bash
dotnet run --project src/Swarm.Cluster
```

Kestrel binds three listeners:

| Port | Protocol | Purpose |
|---|---|---|
| 5000 | HTTP/2 (h2c) | gRPC — Node registration and heartbeats |
| 5001 | HTTP/1.1 | REST API and Swagger UI |
| 5002 | HTTPS | REST API with TLS |

Swagger UI is available at `http://localhost:5001/swagger` in development mode.

## 5. Configure the Node

```bash
cp src/Swarm.Node/appsettings.Development.json.example \
   src/Swarm.Node/appsettings.Development.json
```

Set `ApiKey` (must match what the Cluster expects) and `ClusterUrl` (default: `http://localhost:5000`).

## 6. Start the Node

```bash
dotnet run --project src/Swarm.Node
```

The Node will:
1. Resolve or generate a unique ID from its local SQLite database
2. Connect to the Cluster via gRPC and register
3. Receive RabbitMQ credentials from the Cluster
4. Begin sending heartbeats every `Heartbeat:IntervalSeconds` (default 30s)
5. Start consuming from its task queues

## 7. Start the frontend

```bash
cd src/frontend
npm install
npm run dev
```

Frontend dev server runs at `http://localhost:5173`.

## Running tests

```bash
# All tests
dotnet test Swarm.sln

# With Redis integration tests
$env:SWARM_TEST_REDIS_CONN = "localhost:6379"
dotnet test Swarm.sln

# With Postgres integration tests
$env:SWARM_TEST_POSTGRES_CONN = "Host=localhost;Database=swarm_test;Username=postgres;Password=postgres"
dotnet test Swarm.sln
```

Integration tests skip automatically when their environment variable is not set — the regular unit test run is always clean without infrastructure.

## Adding a migration

After changing a model in `src/Swarm.Cluster/Models/`:

```bash
dotnet ef migrations add <MigrationName> --project src/Swarm.Cluster
dotnet ef database update --project src/Swarm.Cluster
```

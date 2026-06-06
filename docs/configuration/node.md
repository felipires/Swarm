---
title: Node Configuration
---

# Node Configuration

## appsettings.json structure

```json
{
  "ApiKey": "",
  "ClusterUrl": "http://localhost:5000",
  "RabbitMQ": {
    "Hostname": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  },
  "Heartbeat": {
    "IntervalSeconds": 30
  },
  "Swarm": {
    "Tags": {
      "region": "eu",
      "role": "worker"
    },
    "PluginsPath": "/opt/swarm/plugins"
  },
  "Database": {
    "Path": "swarm-node.db"
  }
}
```

## Key settings

### `ApiKey`

The key sent to the Cluster at registration. **Always supply via environment variable — never commit a real key.**

```bash
ApiKey=my-node-api-key dotnet run --project src/Swarm.Node
```

### `ClusterUrl`

The Cluster's gRPC endpoint. Must point at the HTTP/2 port (default 5000). In compose, this is `http://cluster:5000`.

### `Swarm:Tags`

Static tags sent at registration. Combine with `SWARM_TAG_*` environment variables — both are merged.

### `Swarm:PluginsPath`

Directory scanned for plugin assemblies at startup. Each `.dll` is inspected for `ITaskHandler` implementations with a parameterless constructor. See [Plugins →](../sdk/plugins).

### `Database:Path`

Path to the Node's SQLite database. Stores the Node identity UUID and the encrypted env-secret store. Must be on a persistent volume in Docker — losing it means the Node re-registers as a new identity.

## Machine key

The `SWARM_NODE_MACHINE_KEY` environment variable derives the AES-256-GCM key used to encrypt env secrets. If not set:

- A random 32-byte key is generated on first start
- It is written to `.machinekey` next to the SQLite database
- The key is reloaded from `.machinekey` on subsequent starts

**Set `SWARM_NODE_MACHINE_KEY` in production** and back it up — it is required to decrypt previously stored secrets.

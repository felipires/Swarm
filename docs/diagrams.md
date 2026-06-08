# Swarm — Architecture Diagrams

## 1. System Topology

```mermaid
graph TB
    subgraph Operator
        UI[React Dashboard]
    end

    subgraph Cluster["Swarm Cluster (ASP.NET)"]
        REST[REST API]
        GRPC[gRPC Server]
        OUTBOX[Outbox Publisher]
        EXECUTOR[Pipeline Executor]
    end

    subgraph Storage
        PG[(PostgreSQL)]
        REDIS[(Redis)]
        MQ[(RabbitMQ)]
    end

    subgraph Fleet["Node Fleet"]
        N1[Node A]
        N2[Node B]
        N3[Node C]
    end

    UI -->|HTTP| REST
    REST --> PG
    REST --> REDIS
    OUTBOX -->|publish| MQ
    EXECUTOR --> OUTBOX

    N1 -->|gRPC heartbeat / result| GRPC
    N2 -->|gRPC heartbeat / result| GRPC
    N3 -->|gRPC heartbeat / result| GRPC

    N1 -->|consume| MQ
    N2 -->|consume| MQ
    N3 -->|consume| MQ

    GRPC --> PG
    GRPC --> REDIS
```

---

## 2. Task Dispatch Flow

```mermaid
sequenceDiagram
    actor Operator
    participant Cluster
    participant Postgres
    participant RabbitMQ
    participant Node

    Operator->>Cluster: POST /tasks/:id/dispatch
    Cluster->>Postgres: INSERT TaskInstance (Pending)
    Cluster->>Postgres: INSERT PendingDispatch (outbox)
    Cluster-->>Operator: 202 Accepted (instanceId)

    loop Outbox publisher (background)
        Cluster->>Postgres: poll unpublished PendingDispatches
        Cluster->>RabbitMQ: publish TaskMessage → tasks.<nodeId>
        Cluster->>Postgres: mark PendingDispatch.PublishedAt
    end

    RabbitMQ-->>Node: deliver TaskMessage
    Node->>RabbitMQ: publish claim → task-claims
    Node->>Node: resolve config + params
    Node->>Node: execute handler
    Node->>RabbitMQ: publish result → task-results

    loop Result consumer (background)
        Cluster->>RabbitMQ: consume task-results
        Cluster->>Postgres: UPDATE TaskInstance (Completed / Failed + resultJson)
        Cluster->>Postgres: advance pipeline run if applicable
    end
```

---

## 3. Pipeline Execution

```mermaid
flowchart TD
    subgraph Pipeline Definition
        S1[step-1\nextract-users]
        S2[step-2\ncount-rows]
        S3[step-3\nrun-report]
        S1 --> S3
        S2 --> S3
    end

    subgraph Run Lifecycle
        direction LR
        W([Waiting]) --> D([Dispatched])
        D --> C([Completed])
        D --> F([Failed])
        F --> SK([Skipped\ndownstream])
    end

    subgraph Output Mapping
        OM["step-3 reads:\nrowCount → n1  from step-1\nrowCount → n2  from step-2\n\nInjects into runtimeParams:\n{ n1: '42', n2: '17' }"]
    end

    C --> OM
```

---

## 4. Node Registration & Heartbeat

```mermaid
sequenceDiagram
    participant Node
    participant Cluster
    participant Postgres
    participant Redis

    Node->>Cluster: gRPC RegisterNode (name, tags, capabilities, capacity)
    Cluster->>Postgres: UPSERT Node (status=Online, capacity columns)
    Cluster->>Postgres: UPSERT NodeCapabilities
    Cluster-->>Node: nodeId

    loop Every heartbeat interval (default 30s)
        Node->>Cluster: gRPC RecordHeartbeat (nodeId, metrics)
        Cluster->>Postgres: UPDATE Node.LastHeartbeatAt, Status
        Cluster->>Redis: SET node:metrics:latest:{id}  (TTL 300s)
        Cluster->>Redis: LPUSH node:metrics:history:{id} + LTRIM 120
        Cluster-->>Node: pending env ops (key/value or delete)
        Node->>Node: apply env ops to local secret store
    end
```

---

## 5. Value Resolution Pipeline

```mermaid
flowchart LR
    subgraph Input
        CFG["Config template\n(stored on TaskDefinition)\n\n{\n  &quot;query&quot;: &quot;{param:query:required}&quot;,\n  &quot;limit&quot;: {config:rowLimit}\n}"]
        RP["Runtime params\n(per-dispatch or output-mapped)\n\n{\n  &quot;query&quot;: &quot;SELECT {param:n1} + {param:n2};&quot;,\n  &quot;n1&quot;: &quot;10&quot;,\n  &quot;n2&quot;: &quot;20&quot;\n}"]
    end

    subgraph Pre-pass["Pre-pass (self-resolution)"]
        SR["Expand {param:X} refs\nwithin param values\n\nquery → 'SELECT 10 + 20;'"]
    end

    subgraph Resolvers
        ENV[EnvStore resolver\nSWARM_TASKENV_*]
        PARAM[Param resolver\nruntime params]
        CONFIG[Config resolver\nstatic config]
    end

    subgraph Output
        RC["Resolved config\n(passed to handler)\n\n{\n  &quot;query&quot;: &quot;SELECT 10 + 20;&quot;,\n  &quot;limit&quot;: 10\n}"]
    end

    RP --> SR
    SR --> PARAM
    CFG --> CONFIG
    ENV --> RC
    PARAM --> RC
    CONFIG --> RC
```

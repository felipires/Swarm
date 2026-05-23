# Swarm - Distributed ETL Orchestrator: Deep Architecture Review

**Review Date:** March 30, 2026  
**Project Phase:** Phase 1 (Foundation)  
**Status:** Active Development  

---

## Executive Summary

Swarm is a distributed system designed for asynchronous remote task orchestration using low-level communication patterns (gRPC, RabbitMQ) and batch operations. Currently in Phase 1 foundation development with:
- ✅ Node registration and heartbeat mechanisms established
- ✅ Multi-layer communication patterns (gRPC + RabbitMQ) configured
- ✅ Basic data models for cluster coordination
- ⏳ **DAG orchestration: NOT YET IMPLEMENTED** (critical gap)
- ⏳ **Batch operations framework: NOT YET IMPLEMENTED** (planned for Phase 2)
- ⏳ **Task execution pipeline: SKELETON ONLY** (requires implementation)

---

## System Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                   Frontend (React)                       │
│              SSE Streaming + REST API                   │
└──────────────────────┬──────────────────────────────────┘
                       │ HTTP/HTTPS
┌──────────────────────▼──────────────────────────────────┐
│           Cluster (ASP.NET Core + PostgreSQL)            │
│  ┌──────────────────────────────────────────────────┐  │
│  │ REST API + gRPC Services                         │  │
│  │ - Node Registration & Heartbeat (gRPC)          │  │
│  │ - Task Management (REST)                        │  │
│  │ - Execution Orchestration (NOT IMPLEMENTED)     │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Services:                                        │  │
│  │ - NodeService: registration, heartbeat, status  │  │
│  │ - TaskService: (PLANNED)                        │  │
│  │ - ExecutionService: (PLANNED)                   │  │
│  │ - DagOrchestrator: (NOT STARTED)               │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Database: PostgreSQL                             │  │
│  │ - Nodes (registration, status)                  │  │
│  │ - TaskDefinitions (PLANNED)                     │  │
│  │ - TaskInstances (PLANNED)                       │  │
│  │ - ExecutionLogs (PLANNED)                       │  │
│  │ - DagConfigurations (NOT STARTED)              │  │
│  └──────────────────────────────────────────────────┘  │
└──────────────────────┬──────────────────────────────────┘
      ┌───────────────┼───────────────┐
      │               │               │
    gRPC          RabbitMQ        RabbitMQ
      │         Task Dispatch      Result/Log
      │               │               │
┌─────▼───────────────▼───────────────▼─────┐
│   Node (Worker Service + SQLite)           │
│  ┌────────────────────────────────────┐   │
│  │ Main Event Loop (NodeWorker)        │   │
│  │ - Send heartbeats every 120s       │   │
│  │ - Wait for task commands (STUB)    │   │
│  │ - Execute operations (STUB)        │   │
│  │ - Publish results (STUB)           │   │
│  └────────────────────────────────────┘   │
│  ┌────────────────────────────────────┐   │
│  │ Services:                           │   │
│  │ - RegistrationService              │   │
│  │ - HeartBeatService (120s interval) │   │
│  │ - TaskExecutor: (NOT IMPLEMENTED)  │   │
│  │ - QueueListener: (NOT IMPLEMENTED) │   │
│  └────────────────────────────────────┘   │
│  ┌────────────────────────────────────┐   │
│  │ Local State: SQLite                │   │
│  │ - Configuration                    │   │
│  │ - RemoteParameters (queue creds)   │   │
│  │ - LocalTasks (PLANNED)             │   │
│  │ - ScheduledJobs (PLANNED)          │   │
│  └────────────────────────────────────┘   │
│  ┌────────────────────────────────────┐   │
│  │ Background Services:                │   │
│  │ - StartupService (registration)    │   │
│  │ - NodeWorker (heartbeat + exec)    │   │
│  │ - BackgroundMaestro (orchestrator) │   │
│  └────────────────────────────────────┘   │
└─────────────────────────────────────────────┘
```

---

## Current Communication Patterns ✅

### 1. **gRPC Communication** (Implemented)
**Purpose:** Low-latency, strongly-typed node coordination

**Endpoints:**
- `RegisterNode(RegisterNodeRequest) → RegisterNodeResponse`
  - Node sends: `node_id`, `api_key`, `environment_tags`
  - Receives: `node_id`, `node_name`, `queue_parameters`
  - Located: [Cluster/GrpcServices/NodesGrpcService.cs](./Cluster/GrpcServices/NodesGrpcService.cs)

- `RecordHeartbeat(RecordHeartbeatRequest) → RecordHeartbeatResponse`
  - Node sends: `node_id`, `api_key`, `is_online`
  - Cluster updates `LastHeartbeatAt` in database
  - Located: [Node/Services/HeartBeatService.cs](./Node/Services/HeartBeatService.cs)

**Protocol Definition:** [Protos/nodes.proto](./Node/Protos/nodes.proto)

**Channel Configuration:**
- Node connects to Cluster via configured URL
- HTTP/2 protocol on port 5000 (Cluster)
- SSL certificate validation disabled for non-prod (SECURITY CONCERN ⚠️)

### 2. **RabbitMQ Integration** (Configured but Not Operational)
**Purpose:** Reliable async job dispatch and result collection

**Current Status:** ⏳ NOT IMPLEMENTED
- Queue credentials extracted from Cluster config during registration
- Stored in Node's SQLite (`RemoteParameters` table)
- No actual message handlers or producers implemented
- Queue topology (exchanges, queues, bindings) not defined

**Required Configuration:**
```json
{
  "RabbitMQ": {
    "Hostname": "rabbitmq",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

**Planned Use Cases (Not Yet Implemented):**
- Cluster → Node: Task execution commands
- Node → Cluster: Task result publishing
- Node → Cluster: Log streaming
- Cluster → All Nodes: Broadcast commands

### 3. **REST API** (Partially Implemented)
**Purpose:** Administrative task and node management

**Implemented Endpoints:**
- `GET /api/nodes` - List all nodes with optional status filter
- `GET /api/nodes/{id}` - Get specific node details
- `DELETE /api/nodes/{id}` - Remove node registration
- `POST /api/nodes/offline-check` - Trigger heartbeat timeout detection

**Missing Endpoints (Planned Phase 2):**
- `POST /api/tasks` - Create task definition
- `GET /api/tasks/{id}` - Get task details
- `POST /api/tasks/{id}/execute` - Trigger task execution
- `GET /api/executions/{id}` - Get execution status
- `GET /api/executions/{id}/logs` - Stream execution logs

---

## Data Models Analysis

### **Cluster Models**

#### `Node` (Implemented)
```csharp
public class Node
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Status { get; set; } // "online" or "offline"
    public DateTime CreatedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public string EnvironmentTagsJson { get; set; } // JSON serialized
}
```

**Issues:**
- ⚠️ `Status` is string, should be enum for type safety
- ⚠️ No capability tracking beyond environment tags
- ⚠️ No active task assignment tracking

### **Node Models**

#### `LocalTask` (Declared but Empty)
```csharp
public class LocalTask
{
    public Guid Id { get; set; }
    // ... (TO BE IMPLEMENTED)
}
```

#### `ScheduledJob` (Declared)
```csharp
public class ScheduledJob
{
    public Guid Id { get; set; }
    public Guid TaskDefinitionId { get; set; }
    public string CronExpression { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
```

**Database Support:** SQLite with manual migrations ([00.00_Initial.sql](./Node/Data/Migrations/00.00_Initial.sql))

**Planned Models (Phase 2):**
- `TaskDefinition` - Task templates
- `TaskInstance` - Individual task executions
- `DagDefinition` - Workflow specifications
- `DagExecution` - Workflow run instances
- `ExecutionLog` - Operation audit trail

---

## Background Services & Async Operations

### **Cluster Side: HeartbeatBackgroundService** ✅
**File:** [Cluster/Services/HeartbeatBackgroundService.cs](./Cluster/Services/HeartbeatBackgroundService.cs)

**Function:**
```csharp
- Runs on PeriodicTimer (30-second interval)
- Calls NodeService.MarkOfflineNodesAsync()
- Marks nodes offline if heartbeat not received in 300 seconds (5 minutes)
- Uses scoped DI for database access
```

**Implementation Quality:** ✅ Good
- Proper error handling with logging
- Cancellation token support
- Resource cleanup (using PeriodicTimer)

**Concern:** ⚠️ Hard-coded heartbeat timeout (not configurable)

### **Node Side: NodeWorker** ⏳
**File:** [Node/BackgroundServices/NodeWorker.cs](./Node/BackgroundServices/NodeWorker.cs)

**Current Function:**
```csharp
- Waits for BackgroundMaestro signal before starting
- Periodic heartbeat loop (120 seconds)
- Calls HeartBeatService.SendHeartBeatAsync()
- Catches exceptions and restarts loop after 10 seconds
```

**Critical Issues:**
1. ⚠️ **Uses `goto` statement** for error recovery (anti-pattern)
   - Should use `while` with error handling instead
   - Makes code flow hard to follow

2. ⏳ **Task execution loop is missing**
   - Currently only sends heartbeats
   - No RabbitMQ listener implemented
   - Should consume task messages between heartbeats

3. ⚠️ **No graceful shutdown state**
   - No pre-shutdown notification to cluster
   - Should send offline signal before terminating

### **Node Orchestration: BackgroundMaestro** ✅
**File:** [Node/BackgroundServices/BackgroundMaestro.cs](./Node/BackgroundServices/BackgroundMaestro.cs)

**Function:**
```csharp
- Coordinates startup sequence
- Uses TaskCompletionSource for synchronization
- StartupService calls Release() after registration
- NodeWorker waits via WaitAsync() before starting
```

**Implementation:** Sound synchronization pattern using TCS

---

## Authentication & Security

### **API Key Authentication** ✅
**Implementation:** [Cluster/Middleware/ApiKeyAuthMiddleware.cs](./Cluster/Middleware/ApiKeyAuthMiddleware.cs)

**Flow:**
- Expects `X-API-Key` header in all requests
- Validates against configuration
- Swagger integration for key definition

### **Security Concerns** ⚠️

1. **SSL Certificate Validation Disabled**
   ```csharp
   // Node/Program.cs
   ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
   ```
   - Vulnerable to MITM attacks
   - Should only be for development
   - Need certificate pinning for production

2. **No request signing**
   - RabbitMQ messages unencrypted
   - No message integrity verification
   - Vulnerable to tampering

3. **API Key in environment variables**
   - No key rotation mechanism
   - No audit trail for key usage
   - Should implement OAuth2 or similar

---

## Deployment & Infrastructure ✅

### **Docker Compose Stack** (Fully Configured)
**File:** [docker-compose.yml](./docker-compose.yml)

**Services:**
1. **PostgreSQL 16** - Cluster state persistence
   - Database: `swarm_cluster`
   - Credentials: postgres/postgres
   - Volume-mounted for persistence

2. **Redis 7** - Caching layer (configured but not used)
   - Connection string configured in Cluster
   - Currently commented out in services

3. **RabbitMQ 3.12** - Message broker
   - Management UI: http://localhost:15672
   - Credentials: guest/guest
   - Not yet integrated into services

4. **Cluster Service** - ASP.NET Core
   - Port 5000 (HTTP/2 gRPC)
   - Port 5001 (HTTPS)
   - Environment-aware configuration
   - Depends on migrations

5. **Node Service** - Worker service
   - Configurable replicas (default: 1)
   - Depends on Cluster and RabbitMQ
   - Log volume mounted

6. **Frontend Service** - React (config incomplete in snippet)

**Health Checks:** All services properly configured with health checks

---

## Planned Features Status (From README)

### Phase 1: ✅ Complete
- ✅ Project structures
- ✅ Models with UUIDs
- ✅ Database contexts
- ✅ Serilog logging
- ✅ RabbitMQ service stubs
- ✅ SSE infrastructure skeleton
- ✅ Docker Compose orchestration

### Phase 2: ⏳ Todo
- ⏳ Node registration endpoint (gRPC ✅, REST TBD)
- ⏳ Node heartbeat (✅ Implemented)
- ⏳ Capability discovery (environment tags only)
- ⏳ Health check endpoints
- ⏳ Node dashboard UI (scaffolding exists)

### Phase 2+ (Not Started):
- ⏳ **Task execution engine**
- ⏳ **RabbitMQ message handlers**
- ⏳ **DAG orchestration framework** ← CRITICAL
- ⏳ **Batch operation sequencing**
- ⏳ **Distributed transaction support**

---

## Critical Gaps for DAG & Batch Orchestration

### **1. DAG Definition & Storage** ❌
**Required:**
- DAG model: vertices (tasks), edges (dependencies), execution order
- Storage in PostgreSQL Cluster
- Validation engine (cycle detection, topology sorting)
- Version control for DAG definitions

**Current State:** No models, no endpoints, no validation

**Implementation Needed:**
```csharp
// NOT YET IMPLEMENTED
public class DagDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string TaskNodes { get; set; } // JSON: [{id, taskId}, ...]
    public string Edges { get; set; } // JSON: [{from, to}, ...]
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### **2. DAG Execution Engine** ❌
**Required:**
- Topological sort to determine execution order
- Dependency resolution
- State machine for execution tracking
- Failure handling strategy (fail-fast, skip, retry)
- Result aggregation from multiple tasks

**Current State:** No execution engine

### **3. Batch Operations Framework** ❌
**Required:**
- Batch job definition
- Job segmentation (chunking large datasets)
- Parallel execution across nodes
- Result collection and aggregation
- Progress tracking and reporting

**Current State:** No batch framework

### **4. Task Execution Chain** ❌
**Required:**
- Task definition model
- Task instance model
- Execution state tracking
- RabbitMQ message producers/consumers
- Result publishing mechanism

**Current State:** Models exist (empty), no implementation

---

## Code Quality Assessment

### **Strengths** ✅
1. **Proper DI and Configuration Management**
   - IConfiguration, ILogger injected consistently
   - Scoped services for database access
   - Environment-based configuration

2. **Logging Strategy**
   - Serilog integration across all services
   - Structured logging with semantic information
   - Appropriate log levels

3. **Database Design**
   - Proper EF Core migrations
   - UUID for distributed systems
   - Indexed for performance (Status, LastHeartbeatAt)

4. **API Design**
   - REST conventions followed
   - Proper HTTP status codes
   - Clear endpoint naming

5. **Error Handling (Most Services)**
   - Try-catch with logging
   - Grac graceful degradation
   - Appropriate exception types

### **Issues** ⚠️

1. **Anti-Patterns**
   - ❌ Use of `goto` statement in NodeWorker
   - ❌ String-based Status enum
   - ❌ Hard-coded magic numbers (timeout, intervals)

2. **Missing Features**
   - ❌ No transaction support across distributed calls
   - ❌ No idempotency keys for message handlers
   - ❌ No circuit breaker pattern for failure handling

3. **Testing**
   - ❌ No unit tests visible
   - ❌ No integration test infrastructure
   - ❌ No test databases configured

4. **Documentation**
   - ✅ XML docs on some methods
   - ❌ No architecture decision records
   - ❌ No deployment documentation

5. **Observability**
   - ✅ Logging in place
   - ❌ No distributed tracing (OpenTelemetry)
   - ❌ No metrics collection
   - ❌ No APM integration

---

## Configuration Management

### **Current Setup**
- `appsettings.json` for defaults
- Environment variables for overrides
- Secrets manager for sensitive data (some manual setup required)

### **Missing**
- ⏳ Feature flags
- ⏳ Runtime configuration updates (hot reload)
- ⏳ Environment-specific profiles
- ⏳ Configuration validation at startup

---

## Frontend Status

### **Implemented**
- ✅ React + TypeScript + Vite setup
- ✅ Zustand state management
- ✅ API client services
- ✅ Basic component scaffolding

### **Components**
1. **NodeDashboard** - Lists nodes (TODO: implement data binding)
2. **TaskForm** - Creates tasks (TODO: implement submission)
3. **ExecutionMonitor** - Placeholder for execution logs

### **Missing Features**
- ⏳ Real-time updates (SSE configured but not used)
- ⏳ DAG visualization
- ⏳ Execution progress tracking
- ⏳ Error reporting UI

---

## Recommended Implementation Priority

### **Immediate (Critical Path for MVP)**
1. **Fix NodeWorker anti-patterns** (1-2 hours)
   - Replace `goto` with proper loop structure
   - Add graceful shutdown notification

2. **Implement RabbitMQ integration** (4-6 hours)
   - Define queue topology
   - Implement message producers/consumers
   - Add dead-letter handling

3. **Create core Task models** (2-3 hours)
   - Extend LocalTask and TaskDefinition
   - Add TaskInstance for execution tracking
   - Implement repository pattern

4. **Build task execution engine** (6-8 hours)
   - Message handler for task consumption
   - Execution state machine
   - Result publishing

### **High Priority (Phase 2)**
5. **DAG framework** (8-12 hours)
   - DagDefinition model
   - Topological sort algorithm
   - Execution orchestrator
   - Dependency validation

6. **Batch operations** (6-8 hours)
   - Batch model and repository
   - Job segmentation logic
   - Result aggregation

7. **Testing infrastructure** (4-6 hours)
   - Unit test projects
   - Integration test database
   - Test fixtures and data builders

8. **Observability** (4-5 hours)
   - OpenTelemetry integration
   - Distributed tracing
   - Metrics collection

### **Medium Priority (Production-Ready)**
9. **Security hardening** (6-8 hours)
   - Certificate pinning
   - Message signing
   - API key rotation mechanism
   - Encryption at rest

10. **Resilience patterns** (6-8 hours)
    - Circuit breaker implementation
    - Retry policies with exponential backoff
    - Timeout handling
    - Bulkhead isolation

11. **Documentation** (4-6 hours)
    - Architecture decision records
    - API documentation (OpenAPI/Swagger)
    - Deployment guide
    - Troubleshooting guide

---

## Example: Implementing a Simple DAG

Here's a simplified example of what needs implementation:

```csharp
// NOT YET IMPLEMENTED
public class DagOrchestrator
{
    public async Task ExecuteDagAsync(Guid dagId, CancellationToken ct)
    {
        var dag = await _dbContext.DagDefinitions.FindAsync(dagId);
        
        // 1. Parse DAG structure
        var graph = ParseDagJson(dag.TaskNodes, dag.Edges);
        
        // 2. Topological sort to determine execution order
        var executionOrder = TopologicalSort(graph);
        
        // 3. Execute tasks in order
        var results = new Dictionary<Guid, TaskResult>();
        foreach (var taskId in executionOrder)
        {
            var dependencies = graph[taskId].Dependencies;
            var depResults = dependencies.Select(d => results[d]).ToList();
            
            // 4. Dispatch task to available node
            var node = await SelectNodeForTask(taskId);
            var result = await DispatchTaskViaRabbitMQ(node, taskId, depResults, ct);
            
            results[taskId] = result;
            
            // 5. Handle failures
            if (!result.Success && dag.FailureStrategy == FailureStrategy.FailFast)
            {
                throw new DagExecutionException($"Task {taskId} failed");
            }
        }
        
        // 6. Update execution status
        await UpdateDagExecutionStatus(dagId, ExecutionStatus.Completed, results);
    }
}
```

---

## Monitoring & Metrics to Track

### **Key Metrics**
- Node registration/deregistration rate
- Heartbeat success rate and latency
- Task execution time distribution
- DAG completion rate and duration
- Queue depth per task type
- Message publish/consume rate
- Error rates by type

### **Alerting Rules**
- Node heartbeat timeout
- Queue backup (depth > threshold)
- Task execution timeout
- DAG execution failure
- Database connection pool exhaustion

---

## Conclusion

**Swarm is a well-architected foundation** for a distributed orchestration system with:
- ✅ Solid communication patterns (gRPC + RabbitMQ)
- ✅ Proper infrastructure (Docker, PostgreSQL, logging)
- ✅ Promising startup mechanisms

**However, the core orchestration features are not yet implemented:**
- ❌ DAG execution engine
- ❌ Batch operation framework
- ❌ Task execution pipeline

**To achieve the stated goal of "distributed system that runs remote operations asynchronously and orchestrates batch operations with DAGs", critical work remains:**

1. Build task execution engine with RabbitMQ
2. Implement DAG definitions and topological execution
3. Add batch operation support with result aggregation
4. Implement resilience patterns for production readiness
5. Add comprehensive testing and observability

**Estimated effort for MVP: 25-35 hours of development + 5-10 hours of testing/docs**

---

## Next Steps

1. **This week:** Fix NodeWorker anti-patterns + RabbitMQ integration
2. **Next week:** Task execution engine + core DAG framework
3. **Week after:** Batch operations + test infrastructure
4. **Month 2:** Production hardening + distributed tracing

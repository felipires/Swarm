export type NodeHealth = "Healthy" | "Degraded" | "Unhealthy";

/** Live per-node metrics (P5-1), from Redis. Absent until the first sample. */
export interface NodeMetrics {
  recordedAt: string;
  cpuPercent: number;
  memoryUsedBytes: number;
  memoryAvailableBytes: number;
  inFlightTasks: number;
  uptimeSeconds: number;
  health: NodeHealth;
}

export interface Node {
  id: string;
  name: string;
  status: "Online" | "Offline";
  lastHeartbeatAt: string;
  createdAt: string;
  effectiveTags?: Record<string, string>;
  capabilities?: string[];
  // P5-1 capacity + live metrics — render if present, ignore if absent.
  cpuCores?: number | null;
  totalMemoryBytes?: number | null;
  latestMetrics?: NodeMetrics | null;
}

export interface TaskDefinition {
  id: string;
  name: string;
  description?: string;
  configJson: string;
  createdAt: string;
  updatedAt: string;
}

export interface TaskInstance {
  id: string;
  taskDefinitionId: string;
  /** Null until a shared-queue dispatch is claimed by a node. */
  nodeId?: string | null;
  status: "Pending" | "Dispatched" | "Running" | "Completed" | "Failed";
  /** TaskType@version captured at dispatch (P1-4). */
  taskType?: string;
  /** Config sent to the node, snapshotted at dispatch (P1-4). */
  configJsonSnapshot?: string | null;
  /** Per-run runtime params resolved into the dispatch (P1-6). */
  runtimeParamsJson?: string | null;
  resultJson?: string;
  errorMessage?: string;
  createdAt: string;
  dispatchedAt?: string;
  completedAt?: string;
}

export type DispatchStrategy =
  | "SpecificNode"
  | "AllOnlineNodes"
  | "AnyOnlineNode"
  | "TaggedNodes";

export type FailurePolicy = "FailPipeline" | "ContinuePipeline";

/** Read model — matches PipelineStepResponse. `dependsOn` holds step IDs (Guid)
 *  and `strategyOverride` is null when the step inherits the default strategy. */
export interface PipelineStep {
  id: string;
  name: string;
  order: number;
  dependsOn: string[];
  strategyOverride?: DispatchStrategy | null;
  targetNodeId?: string;
  targetTagsJson?: string | null;
  failurePolicy: FailurePolicy;
  taskDefinitionId: string;
}

export interface Pipeline {
  id: string;
  name: string;
  description?: string;
  version: number;
  createdAt: string;
  updatedAt: string;
  steps: PipelineStep[];
}

/** Write model — matches CreatePipelineStep. `dependsOn` here is step NAMES. */
export interface DraftPipelineStep {
  name: string;
  taskDefinitionId: string;
  dependsOn: string[];
  strategy?: DispatchStrategy | null;
  targetNodeId?: string | null;
  targetTags?: Record<string, string> | null;
  failurePolicy: FailurePolicy;
  order: number;
}

export interface CreatePipelineRequest {
  name: string;
  description?: string | null;
  steps: DraftPipelineStep[];
}

/** Matches the backend DispatchTaskRequest — every field optional; omitted
 *  fields fall back to the TaskDefinition's defaults. */
export interface DispatchRequest {
  nodeId?: string | null;
  strategy?: DispatchStrategy | null;
  targetTags?: Record<string, string> | null;
  runtimeParams?: Record<string, unknown> | null;
}

export type PipelineStepInstanceStatus =
  | "Waiting"
  | "Dispatched"
  | "Completed"
  | "Failed"
  | "Skipped";

export interface PipelineStepInstance {
  id: string;
  pipelineRunId: string;
  pipelineStepId: string;
  taskInstanceId?: string | null;
  status: PipelineStepInstanceStatus;
  createdAt: string;
  dispatchedAt?: string;
  completedAt?: string;
  errorMessage?: string;
}

export type PipelineRunStatus = "Running" | "Completed" | "Failed" | "Cancelled";

export interface PipelineRun {
  id: string;
  pipelineId: string;
  status: PipelineRunStatus;
  startedAt: string;
  completedAt?: string;
  errorMessage?: string;
}

export interface Schedule {
  id: string;
  pipelineId: string;
  cronExpression: string;
  timeZoneId: string;
  enabled: boolean;
  lastFiredAt?: string;
  nextFireAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateScheduleRequest {
  cronExpression: string;
  timeZoneId?: string;
  enabled?: boolean;
  runtimeParams?: Record<string, unknown>;
}

export interface UpdateScheduleRequest {
  cronExpression?: string;
  timeZoneId?: string;
  enabled?: boolean;
  runtimeParams?: Record<string, unknown>;
}

export interface CursorPage<T> {
  items: T[];
  hasMore: boolean;
  nextCursor?: string;
}

/** A subset of JSON Schema (draft 2020-12) the handler config validator reads.
 *  Handlers may emit richer schemas; the UI uses what it understands. */
export interface JsonSchema {
  type?: string;
  required?: string[];
  properties?: Record<string, JsonSchema>;
  items?: JsonSchema;
  [key: string]: unknown;
}

export interface CapabilityCatalogEntry {
  taskType: string;
  /** JSON Schema string describing the handler's ConfigJson. */
  jsonSchema: string;
  requiredEnvKeys: string[];
  requiredParams: string[];
  nodeCount: number;
}

export type LogLevel = "Debug" | "Information" | "Warning" | "Error" | "Critical";

/** Matches the SSE payload emitted by LogsController.WriteLogAsync:
 *  { id, nodeId, level, message, timestamp, exception }. `level` is a free-form
 *  string from the worker's logger, so consumers must tolerate unknown values. */
export interface LogEntry {
  id: string;
  nodeId: string;
  level: string;
  message: string;
  timestamp: string;
  exception?: string | null;
}

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
  /** Backend may include taskType/defaultStrategy; kept optional for the editor. */
  taskType?: string;
  defaultStrategy?: DispatchStrategy;
  defaultTargetTagsJson?: string | null;
  version?: number;
  isDeleted?: boolean;
  deletedAt?: string | null;
  createdAt: string;
  updatedAt: string;
}

/** A version-history entry (P1-10). List responses omit `snapshot`; the
 *  single-version endpoint includes it (the create-request-shaped definition). */
export interface EntityVersionEntry {
  version: number;
  createdAt: string;
  snapshot?: unknown;
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

/** P1-8: extracts a value from an upstream step's result JSON and injects it as
 *  a runtime param into this step before dispatch. */
export interface OutputMapping {
  /** Name of an upstream (ancestor) step. */
  fromStep: string;
  /** Dot/bracket path into that step's result JSON, e.g. `rows[0].email`. */
  fromPath: string;
  /** Runtime param key the extracted value is injected as. */
  toParam: string;
}

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
  outputMappings?: OutputMapping[] | null;
  /** P1-9: literal params authored on this step (JSON string). */
  runtimeParamsJson?: string | null;
}

export interface Pipeline {
  id: string;
  name: string;
  description?: string;
  version: number;
  isDeleted?: boolean;
  deletedAt?: string | null;
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
  outputMappings?: OutputMapping[];
  /** P1-9: literal per-step params; serialized to RuntimeParamsJson server-side. */
  runtimeParams?: Record<string, unknown> | null;
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
  resultJson?: string | null;
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

/** A persisted log row from the search endpoint (`GET /api/logs`). `level` is a
 *  free-form string from the worker's logger, so consumers must tolerate unknown
 *  values. `tags` is the correlation/context map (task / run / step / pipeline /
 *  env.*) used for faceted filtering and clickable chips. */
export interface LogRecord {
  id: string;
  nodeId?: string | null;
  level: string;
  messageTemplate: string;
  message?: string | null;
  exception?: string | null;
  tags?: Record<string, string> | null;
  timestamp: string;
}

/** Structured filters for the log search endpoint. */
export interface LogQueryParams {
  /** Repeated `key:value` tag facets, AND-combined (e.g. ["run:abc","level…"]). */
  tags?: string[];
  /** Concrete level names to include (e.g. ["Warning","Error"]). */
  level?: string[];
  /** Free-text substring over message and template. */
  q?: string;
  nodeId?: string;
  /** ISO UTC bounds. */
  from?: string;
  to?: string;
}

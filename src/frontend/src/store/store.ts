export interface Node {
  id: string;
  name: string;
  status: "Online" | "Offline";
  lastHeartbeatAt: string;
  createdAt: string;
  // Planned backend enrichment — render if present, ignore if absent.
  effectiveTags?: Record<string, string>;
  capabilities?: string[];
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
  nodeId: string;
  status: "Pending" | "Dispatched" | "Running" | "Completed" | "Failed";
  resultJson?: string;
  errorMessage?: string;
  createdAt: string;
  dispatchedAt?: string;
  completedAt?: string;
}

export type DispatchStrategy = "AnyOnline" | "SpecificNode" | "Tagged";
export type FailurePolicy = "Fail" | "Continue" | "Retry";

export interface PipelineStep {
  id: string;
  name: string;
  order: number;
  dependsOn: string[];
  strategy: DispatchStrategy;
  targetNodeId?: string;
  targetTags?: Record<string, string>;
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

export interface CursorPage<T> {
  items: T[];
  hasMore: boolean;
  nextCursor?: string;
}

export type LogLevel = "Debug" | "Information" | "Warning" | "Error" | "Critical";

export interface LogEntry {
  id: string;
  nodeId: string;
  level: LogLevel;
  messageTemplate: string;
  renderedMessage?: string;
  timestamp: string;
  createdAt: string;
}

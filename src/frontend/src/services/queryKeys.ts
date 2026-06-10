export const queryKeys = {
  capabilities: ["capabilities"] as const,
  nodes: ["nodes"] as const,
  node: (id: string) => ["nodes", id] as const,
  nodeEnv: (id: string) => ["nodes", id, "env"] as const,
  nodeMetrics: (id: string) => ["nodes", id, "metrics"] as const,
  tasks: ["tasks"] as const,
  taskInstances: (taskId: string) => ["tasks", taskId, "instances"] as const,
  pipelines: ["pipelines"] as const,
  pipeline: (id: string) => ["pipelines", id] as const,
  pipelineVersions: (id: string) => ["pipelines", id, "versions"] as const,
  pipelineRuns: (pipelineId: string) => ["pipelines", pipelineId, "runs"] as const,
  pipelineRunSteps: (runId: string) => ["pipelines", "runs", runId, "steps"] as const,
  taskInstance: (instanceId: string) => ["tasks", "instances", instanceId] as const,
  taskVersions: (id: string) => ["tasks", id, "versions"] as const,
  pipelineSchedules: (pipelineId: string) => ["pipelines", pipelineId, "schedules"] as const,
  // Log search — keyed by the structured params so each distinct query/filter
  // set is cached independently.
  logs: (params: unknown) => ["logs", params] as const,
};

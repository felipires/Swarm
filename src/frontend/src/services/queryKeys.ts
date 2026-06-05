export const queryKeys = {
  nodes: ["nodes"] as const,
  node: (id: string) => ["nodes", id] as const,
  tasks: ["tasks"] as const,
  taskInstances: (taskId: string) => ["tasks", taskId, "instances"] as const,
  pipelines: ["pipelines"] as const,
  pipelineRuns: (pipelineId: string) => ["pipelines", pipelineId, "runs"] as const,
  pipelineSchedules: (pipelineId: string) => ["pipelines", pipelineId, "schedules"] as const,
};

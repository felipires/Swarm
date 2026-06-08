import axios, { AxiosInstance } from "axios";
import { loadSettings, type ApiSettings } from "./settings";
import type {
  CapabilityCatalogEntry,
  CreatePipelineRequest,
  CreateScheduleRequest,
  CursorPage,
  DispatchRequest,
  DispatchStrategy,
  EntityVersionEntry,
  Node,
  NodeMetrics,
  Pipeline,
  PipelineRun,
  PipelineStepInstance,
  Schedule,
  TaskDefinition,
  TaskInstance,
  UpdateScheduleRequest,
} from "../store/store";

class ApiClient {
  private client: AxiosInstance;

  constructor() {
    const settings = loadSettings();
    this.client = axios.create({ baseURL: settings.baseUrl });
    this.applySettings(settings);
  }

  /** Updates the live axios instance so settings changes take effect without a
   *  page reload. */
  applySettings(settings: ApiSettings): void {
    this.client.defaults.baseURL = settings.baseUrl;
    this.client.defaults.headers.common["Content-Type"] = "application/json";
    if (settings.apiKey) {
      this.client.defaults.headers.common["X-API-Key"] = settings.apiKey;
    } else {
      delete this.client.defaults.headers.common["X-API-Key"];
    }
  }

  /** Pings the server's /health (sibling of the /api base) with the given
   *  settings without mutating the live client. Returns true on { status: "ok" }. */
  async checkHealth(settings: ApiSettings): Promise<boolean> {
    const healthUrl = settings.baseUrl.replace(/\/api\/?$/, "") + "/health";
    const headers = settings.apiKey ? { "X-API-Key": settings.apiKey } : undefined;
    const response = await axios.get(healthUrl, { headers, timeout: 5000 });
    return response.data?.status === "ok";
  }

  // Nodes
  async getNodes(status?: "Online" | "Offline"): Promise<Node[]> {
    const response = await this.client.get("/nodes", {
      params: status ? { status } : {},
    });
    return response.data.items;
  }

  async getNode(id: string): Promise<Node> {
    const response = await this.client.get(`/nodes/${id}`);
    return response.data;
  }

  async deleteNode(id: string): Promise<void> {
    await this.client.delete(`/nodes/${id}`);
  }

  /** Recent metrics history for a node (newest-first, up to 120 samples). */
  async getNodeMetrics(id: string): Promise<NodeMetrics[]> {
    const response = await this.client.get(`/nodes/${id}/metrics`);
    return response.data;
  }

  /** Add/remove overlay tags. Returns the node's effective tags after the change. */
  async updateNodeTags(
    id: string,
    body: { add?: Record<string, string>; remove?: string[] },
  ): Promise<Record<string, string>> {
    const response = await this.client.patch(`/nodes/${id}/tags`, body);
    return response.data;
  }

  /** Queue an env secret for delivery to the node on its next heartbeat. */
  async setNodeEnv(id: string, key: string, value: string): Promise<void> {
    await this.client.post(`/nodes/${id}/env`, { key, value });
  }

  /** Queue an env-key deletion for the node. */
  async deleteNodeEnv(id: string, key: string): Promise<void> {
    await this.client.delete(`/nodes/${id}/env/${encodeURIComponent(key)}`);
  }

  /** List env keys still pending delivery to the node (not the applied set). */
  async getNodeEnvKeys(id: string): Promise<string[]> {
    const response = await this.client.get(`/nodes/${id}/env`);
    return response.data;
  }

  // Tasks
  async getTasks(includeDeleted = false): Promise<TaskDefinition[]> {
    const response = await this.client.get("/tasks", {
      params: includeDeleted ? { includeDeleted: true } : {},
    });
    return response.data.items;
  }

  async getTask(id: string): Promise<TaskDefinition> {
    const response = await this.client.get(`/tasks/${id}`);
    return response.data;
  }

  async createTask(req: {
    name: string;
    description: string;
    taskType: string;
    configJson: string;
    defaultStrategy?: DispatchStrategy;
    defaultTargetTags?: Record<string, string> | null;
  }): Promise<TaskDefinition> {
    const response = await this.client.post("/tasks", req);
    return response.data;
  }

  async updateTask(
    id: string,
    req: {
      name: string;
      description: string;
      taskType: string;
      configJson: string;
      defaultStrategy?: DispatchStrategy;
      defaultTargetTags?: Record<string, string> | null;
      expectedVersion?: number;
    },
  ): Promise<TaskDefinition> {
    const response = await this.client.put(`/tasks/${id}`, req);
    return response.data;
  }

  async getTaskVersions(id: string): Promise<EntityVersionEntry[]> {
    const response = await this.client.get(`/tasks/${id}/versions`);
    return response.data;
  }

  async getTaskVersion(id: string, version: number): Promise<EntityVersionEntry> {
    const response = await this.client.get(`/tasks/${id}/versions/${version}`);
    return response.data;
  }

  async restoreTaskVersion(id: string, version: number): Promise<TaskDefinition> {
    const response = await this.client.post(`/tasks/${id}/versions/${version}/restore`);
    return response.data;
  }

  async undeleteTask(id: string): Promise<void> {
    await this.client.post(`/tasks/${id}/undelete`);
  }

  async deleteTask(id: string): Promise<void> {
    await this.client.delete(`/tasks/${id}`);
  }

  // Dispatch
  async dispatchTask(taskId: string, req: DispatchRequest): Promise<TaskInstance> {
    const response = await this.client.post(`/tasks/${taskId}/dispatch`, req);
    return response.data;
  }

  async dispatchTaskToAll(taskId: string): Promise<TaskInstance[]> {
    const response = await this.client.post(`/tasks/${taskId}/dispatch-all`);
    return response.data;
  }

  // Instances
  async getInstances(taskId: string): Promise<TaskInstance[]> {
    const response = await this.client.get(`/tasks/${taskId}/instances`);
    return response.data.items;
  }

  async getInstance(instanceId: string): Promise<TaskInstance> {
    const response = await this.client.get(`/tasks/instances/${instanceId}`);
    return response.data;
  }

  // Capabilities
  async getCapabilities(): Promise<CapabilityCatalogEntry[]> {
    const response = await this.client.get("/capabilities");
    return response.data;
  }

  // Pipelines
  async getPipelines(includeDeleted = false): Promise<Pipeline[]> {
    const response = await this.client.get("/pipelines", {
      params: includeDeleted ? { includeDeleted: true } : {},
    });
    return response.data.items;
  }

  async getPipeline(id: string): Promise<Pipeline> {
    const response = await this.client.get(`/pipelines/${id}`);
    return response.data;
  }

  async createPipeline(req: CreatePipelineRequest): Promise<Pipeline> {
    const response = await this.client.post("/pipelines", req);
    return response.data;
  }

  async updatePipeline(
    id: string,
    req: CreatePipelineRequest & { expectedVersion?: number },
  ): Promise<Pipeline> {
    const response = await this.client.put(`/pipelines/${id}`, req);
    return response.data;
  }

  async getPipelineVersions(id: string): Promise<EntityVersionEntry[]> {
    const response = await this.client.get(`/pipelines/${id}/versions`);
    return response.data;
  }

  async getPipelineVersion(id: string, version: number): Promise<EntityVersionEntry> {
    const response = await this.client.get(`/pipelines/${id}/versions/${version}`);
    return response.data;
  }

  async restorePipelineVersion(id: string, version: number): Promise<Pipeline> {
    const response = await this.client.post(`/pipelines/${id}/versions/${version}/restore`);
    return response.data;
  }

  async deletePipeline(id: string): Promise<void> {
    await this.client.delete(`/pipelines/${id}`);
  }

  async undeletePipeline(id: string): Promise<void> {
    await this.client.post(`/pipelines/${id}/undelete`);
  }

  async retryFailedRun(runId: string): Promise<PipelineRun> {
    const response = await this.client.post(`/pipelines/runs/${runId}/retry-failed`);
    return response.data;
  }

  async getPipelineRuns(
    pipelineId: string,
    after?: string,
  ): Promise<CursorPage<PipelineRun>> {
    const response = await this.client.get(`/pipelines/${pipelineId}/runs`, {
      params: after ? { after } : {},
    });
    return response.data;
  }

  async getRunSteps(runId: string): Promise<PipelineStepInstance[]> {
    const response = await this.client.get(`/pipelines/runs/${runId}/steps`);
    return response.data;
  }

  async runPipeline(
    pipelineId: string,
    runtimeParams?: Record<string, unknown>,
  ): Promise<PipelineRun> {
    const response = await this.client.post(`/pipelines/${pipelineId}/run`, {
      runtimeParams,
    });
    return response.data;
  }

  async getSchedules(pipelineId: string): Promise<Schedule[]> {
    const response = await this.client.get(
      `/pipelines/${pipelineId}/schedules`,
    );
    return response.data;
  }

  async createSchedule(
    pipelineId: string,
    req: CreateScheduleRequest,
  ): Promise<Schedule> {
    const response = await this.client.post(
      `/pipelines/${pipelineId}/schedules`,
      req,
    );
    return response.data;
  }

  async updateSchedule(
    scheduleId: string,
    req: UpdateScheduleRequest,
  ): Promise<Schedule> {
    const response = await this.client.patch(
      `/pipelines/schedules/${scheduleId}`,
      req,
    );
    return response.data;
  }

  async deleteSchedule(scheduleId: string): Promise<void> {
    await this.client.delete(`/pipelines/schedules/${scheduleId}`);
  }

  // Logs
  logStreamUrl(nodeId: string): string {
    // Use a relative path so the Vite dev proxy forwards to the correct REST port.
    // An absolute BASE_URL would bypass the proxy and hit the gRPC-only port.
    return `/api/logs/stream/${nodeId}`;
  }
}

export const apiClient = new ApiClient();

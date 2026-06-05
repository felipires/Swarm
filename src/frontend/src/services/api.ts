import axios, { AxiosInstance } from "axios";
import { loadSettings, type ApiSettings } from "./settings";
import type {
  CursorPage,
  Node,
  Pipeline,
  PipelineRun,
  Schedule,
  TaskDefinition,
  TaskInstance,
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

  // Tasks
  async getTasks(): Promise<TaskDefinition[]> {
    const response = await this.client.get("/tasks");
    return response.data.items;
  }

  async getTask(id: string): Promise<TaskDefinition> {
    const response = await this.client.get(`/tasks/${id}`);
    return response.data;
  }

  async createTask(
    name: string,
    description: string,
    configJson: string,
  ): Promise<TaskDefinition> {
    const response = await this.client.post("/tasks", {
      name,
      description,
      configJson,
    });
    return response.data;
  }

  async deleteTask(id: string): Promise<void> {
    await this.client.delete(`/tasks/${id}`);
  }

  // Dispatch
  async dispatchTask(taskId: string, nodeId: string): Promise<TaskInstance> {
    const response = await this.client.post(`/tasks/${taskId}/dispatch`, {
      nodeId,
    });
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

  // Pipelines
  async getPipelines(): Promise<Pipeline[]> {
    const response = await this.client.get("/pipelines");
    return response.data.items;
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

  async runPipeline(
    pipelineId: string,
    runtimeParams?: Record<string, string>,
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

  // Logs
  logStreamUrl(nodeId: string): string {
    // Use a relative path so the Vite dev proxy forwards to the correct REST port.
    // An absolute BASE_URL would bypass the proxy and hit the gRPC-only port.
    return `/api/logs/stream/${nodeId}`;
  }
}

export const apiClient = new ApiClient();

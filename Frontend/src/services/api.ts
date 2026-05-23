import axios, { AxiosInstance } from "axios";
import type { Node, TaskDefinition, TaskInstance } from "../store/store";

const BASE_URL = (import.meta as any).env.VITE_API_URL || "http://localhost:5001/api";

class ApiClient {
  private client: AxiosInstance;

  constructor() {
    this.client = axios.create({
      baseURL: BASE_URL,
      headers: { "Content-Type": "application/json" },
    });
  }

  // Nodes
  async getNodes(status?: "Online" | "Offline"): Promise<Node[]> {
    const response = await this.client.get("/nodes", { params: status ? { status } : {} });
    return response.data;
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
    return response.data;
  }

  async getTask(id: string): Promise<TaskDefinition> {
    const response = await this.client.get(`/tasks/${id}`);
    return response.data;
  }

  async createTask(name: string, description: string, configJson: string): Promise<TaskDefinition> {
    const response = await this.client.post("/tasks", { name, description, configJson });
    return response.data;
  }

  async deleteTask(id: string): Promise<void> {
    await this.client.delete(`/tasks/${id}`);
  }

  // Dispatch
  async dispatchTask(taskId: string, nodeId: string): Promise<TaskInstance> {
    const response = await this.client.post(`/tasks/${taskId}/dispatch`, { nodeId });
    return response.data;
  }

  async dispatchTaskToAll(taskId: string): Promise<TaskInstance[]> {
    const response = await this.client.post(`/tasks/${taskId}/dispatch-all`);
    return response.data;
  }

  // Instances
  async getInstances(taskId: string): Promise<TaskInstance[]> {
    const response = await this.client.get(`/tasks/${taskId}/instances`);
    return response.data;
  }

  async getInstance(instanceId: string): Promise<TaskInstance> {
    const response = await this.client.get(`/tasks/instances/${instanceId}`);
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

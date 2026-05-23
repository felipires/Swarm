import { create } from "zustand";

export interface Node {
  id: string;
  name: string;
  status: "Online" | "Offline";
  lastHeartbeatAt: string;
  createdAt: string;
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

interface StoreState {
  nodes: Node[];
  tasks: TaskDefinition[];
  instances: TaskInstance[];
  selectedNode: Node | null;
  selectedTask: TaskDefinition | null;

  setNodes: (nodes: Node[]) => void;
  setTasks: (tasks: TaskDefinition[]) => void;
  setInstances: (instances: TaskInstance[]) => void;
  upsertInstance: (instance: TaskInstance) => void;
  setSelectedNode: (node: Node | null) => void;
  setSelectedTask: (task: TaskDefinition | null) => void;
}

export const useStore = create<StoreState>((set) => ({
  nodes: [],
  tasks: [],
  instances: [],
  selectedNode: null,
  selectedTask: null,

  setNodes: (nodes) => set({ nodes }),
  setTasks: (tasks) => set({ tasks }),
  setInstances: (instances) => set({ instances }),
  upsertInstance: (instance) =>
    set((s) => ({
      instances: s.instances.some((i) => i.id === instance.id)
        ? s.instances.map((i) => (i.id === instance.id ? instance : i))
        : [...s.instances, instance],
    })),
  setSelectedNode: (node) => set({ selectedNode: node }),
  setSelectedTask: (task) => set({ selectedTask: task }),
}));

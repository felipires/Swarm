import React, { useEffect, useState } from "react";
import { apiClient } from "../services/api";
import { useStore, type TaskDefinition, type TaskInstance, type Node } from "../store/store";

function InstanceStatusBadge({ status }: { status: TaskInstance["status"] }) {
  const colors: Record<string, string> = {
    Pending: "bg-gray-100 text-gray-600",
    Dispatched: "bg-blue-100 text-blue-700",
    Running: "bg-yellow-100 text-yellow-700",
    Completed: "bg-green-100 text-green-700",
    Failed: "bg-red-100 text-red-700",
  };
  return <span className={`px-2 py-0.5 rounded text-xs font-medium ${colors[status] ?? "bg-gray-100 text-gray-600"}`}>{status}</span>;
}

interface TaskPanelProps {
  task: TaskDefinition;
  nodes: Node[];
  onDelete: (id: string) => void;
}

function TaskPanel({ task, nodes, onDelete }: TaskPanelProps) {
  const { upsertInstance } = useStore();
  const [instances, setInstances] = useState<TaskInstance[]>([]);
  const [dispatching, setDispatching] = useState(false);
  const [open, setOpen] = useState(false);

  const loadInstances = async () => {
    const data = await apiClient.getInstances(task.id);
    setInstances(data);
    data.forEach((i) => upsertInstance(i));
  };

  useEffect(() => {
    if (open) loadInstances();
  }, [open]);

  const dispatchAll = async () => {
    setDispatching(true);
    try {
      const result = await apiClient.dispatchTaskToAll(task.id);
      result.forEach((i) => upsertInstance(i));
      await loadInstances();
    } catch (e: any) {
      alert(e?.response?.data?.error ?? e?.message ?? "Dispatch failed");
    } finally {
      setDispatching(false);
    }
  };

  const dispatchTo = async (nodeId: string) => {
    setDispatching(true);
    try {
      const result = await apiClient.dispatchTask(task.id, nodeId);
      upsertInstance(result);
      await loadInstances();
    } catch (e: any) {
      alert(e?.response?.data?.error ?? e?.message ?? "Dispatch failed");
    } finally {
      setDispatching(false);
    }
  };

  const onlineNodes = nodes.filter((n) => n.status === "Online");

  return (
    <div className="border border-gray-200 rounded-lg overflow-hidden">
      <div
        className="flex items-center justify-between px-4 py-3 bg-gray-50 cursor-pointer hover:bg-gray-100 transition-colors"
        onClick={() => setOpen((o) => !o)}
      >
        <div className="flex items-center gap-3">
          <span className="text-gray-400 text-xs">{open ? "▼" : "▶"}</span>
          <span className="font-medium text-gray-900 text-sm">{task.name}</span>
          {task.description && <span className="text-gray-400 text-xs">{task.description}</span>}
        </div>
        <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
          <button
            onClick={dispatchAll}
            disabled={dispatching || onlineNodes.length === 0}
            className="btn-primary text-xs py-1 px-3 disabled:opacity-50"
          >
            {dispatching ? "Dispatching…" : `Run on all (${onlineNodes.length})`}
          </button>
          {onlineNodes.length > 1 && (
            <select
              className="input-field text-xs py-1 w-auto"
              defaultValue=""
              onChange={(e) => { if (e.target.value) dispatchTo(e.target.value); }}
              disabled={dispatching}
            >
              <option value="" disabled>Run on node…</option>
              {onlineNodes.map((n) => (
                <option key={n.id} value={n.id}>{n.name}</option>
              ))}
            </select>
          )}
          <button
            onClick={() => onDelete(task.id)}
            className="text-red-400 hover:text-red-600 text-xs px-2 py-1 rounded hover:bg-red-50 transition-colors"
          >
            Delete
          </button>
        </div>
      </div>

      {open && (
        <div className="p-4 space-y-3">
          <div className="flex items-center justify-between">
            <span className="text-xs text-gray-500 font-medium uppercase tracking-wide">Executions</span>
            <button onClick={loadInstances} className="text-xs text-blue-500 hover:underline">Refresh</button>
          </div>
          {instances.length === 0 ? (
            <p className="text-xs text-gray-400">No executions yet. Click "Run on all" to dispatch.</p>
          ) : (
            <div className="divide-y divide-gray-100 rounded border border-gray-100 overflow-hidden">
              {instances.map((inst) => {
                const nodeName = nodes.find((n) => n.id === inst.nodeId)?.name ?? inst.nodeId.slice(0, 8);
                return (
                  <div key={inst.id} className="flex items-center gap-4 px-3 py-2 text-xs bg-white hover:bg-gray-50">
                    <InstanceStatusBadge status={inst.status} />
                    <span className="text-gray-600 font-medium">{nodeName}</span>
                    <span className="text-gray-400">{new Date(inst.createdAt).toLocaleTimeString()}</span>
                    {inst.completedAt && (
                      <span className="text-gray-400">
                        → {new Date(inst.completedAt).toLocaleTimeString()}
                      </span>
                    )}
                    {inst.errorMessage && (
                      <span className="text-red-500 truncate max-w-xs" title={inst.errorMessage}>⚠ {inst.errorMessage}</span>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export const TaskManager: React.FC = () => {
  const { tasks, setTasks, nodes } = useStore();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [configJson, setConfigJson] = useState("{}");
  const [configError, setConfigError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [loading, setLoading] = useState(true);

  const fetchTasks = async () => {
    try {
      const data = await apiClient.getTasks();
      setTasks(data);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { fetchTasks(); }, []);

  const validateConfig = (v: string) => {
    try { JSON.parse(v); setConfigError(null); } catch { setConfigError("Invalid JSON"); }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    try { JSON.parse(configJson); } catch { setConfigError("Invalid JSON"); return; }
    setSubmitting(true);
    try {
      const task = await apiClient.createTask(name.trim(), description.trim(), configJson);
      setTasks([task, ...tasks]);
      setName("");
      setDescription("");
      setConfigJson("{}");
    } catch (e: any) {
      alert(e?.response?.data?.error ?? e?.message ?? "Failed to create task");
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm("Delete this task definition?")) return;
    await apiClient.deleteTask(id);
    setTasks(tasks.filter((t) => t.id !== id));
  };

  return (
    <div className="space-y-6">
      <form onSubmit={handleCreate} className="space-y-4 p-4 bg-gray-50 rounded-lg border border-gray-200">
        <h3 className="font-semibold text-gray-800 text-sm uppercase tracking-wide">New Task Definition</h3>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="label">Name *</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="input-field"
              placeholder="e.g., Daily Sync"
              required
            />
          </div>
          <div>
            <label className="label">Description</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="input-field"
              placeholder="Optional"
            />
          </div>
        </div>
        <div>
          <label className="label">Config JSON</label>
          <textarea
            value={configJson}
            onChange={(e) => { setConfigJson(e.target.value); validateConfig(e.target.value); }}
            className={`input-field font-mono text-sm h-20 resize-none ${configError ? "border-red-400 focus:ring-red-400" : ""}`}
          />
          {configError && <p className="text-xs text-red-500 mt-1">{configError}</p>}
        </div>
        <button type="submit" disabled={submitting || !!configError} className="btn-primary disabled:opacity-50">
          {submitting ? "Creating…" : "Create Task"}
        </button>
      </form>

      <div className="space-y-2">
        {loading ? (
          <p className="text-sm text-gray-500">Loading tasks…</p>
        ) : tasks.length === 0 ? (
          <p className="text-sm text-gray-400">No tasks yet. Create one above.</p>
        ) : (
          tasks.map((task) => (
            <TaskPanel key={task.id} task={task} nodes={nodes} onDelete={handleDelete} />
          ))
        )}
      </div>
    </div>
  );
};

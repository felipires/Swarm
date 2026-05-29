import React, { useEffect, useState } from "react";
import { apiClient } from "../services/api";
import { useStore, type Node } from "../store/store";

function StatusBadge({ status }: { status: Node["status"] }) {
  const online = status === "Online";
  return (
    <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium ${online ? "bg-green-100 text-green-800" : "bg-gray-200 text-gray-600"}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${online ? "bg-green-500 animate-pulse" : "bg-gray-400"}`} />
      {status}
    </span>
  );
}

function timeAgo(iso: string): string {
  const diff = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (diff < 60) return `${diff}s ago`;
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  return `${Math.floor(diff / 3600)}h ago`;
}

interface NodeDashboardProps {
  onSelectNode: (node: Node) => void;
  selectedNodeId?: string;
}

export const NodeDashboard: React.FC<NodeDashboardProps> = ({ onSelectNode, selectedNodeId }) => {
  const { nodes, setNodes } = useStore();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchNodes = async () => {
    try {
      const data = await apiClient.getNodes();
      setNodes(data);
      setError(null);
    } catch (e: any) {
      setError(e?.message ?? "Failed to load nodes");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchNodes();
    const interval = setInterval(fetchNodes, 1000 * 60 * 30);
    return () => clearInterval(interval);
  }, []);

  const handleDelete = async (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    if (!confirm("Remove this node from the cluster?")) return;
    await apiClient.deleteNode(id);
    fetchNodes();
  };

  if (loading) return <p className="text-sm text-gray-500 py-4">Loading nodes…</p>;
  if (error) return <p className="text-sm text-red-600 py-4">{error}</p>;

  const online = nodes.filter((n) => n.status === "Online").length;

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3 text-sm text-gray-600">
        <span>{nodes.length} registered</span>
        <span className="text-green-600 font-medium">{online} online</span>
        <span className="text-gray-400">{nodes.length - online} offline</span>
      </div>

      {nodes.length === 0 ? (
        <p className="text-sm text-gray-500 py-6 text-center">No nodes registered yet. Start a Node worker to see it here.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-gray-200">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wider text-xs">Name</th>
                <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wider text-xs">Status</th>
                <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wider text-xs">Last Heartbeat</th>
                <th className="px-4 py-3 text-left font-medium text-gray-500 uppercase tracking-wider text-xs">ID</th>
                <th className="px-4 py-3" />
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-100">
              {nodes.map((node) => (
                <tr
                  key={node.id}
                  onClick={() => onSelectNode(node)}
                  className={`cursor-pointer transition-colors hover:bg-blue-50 ${selectedNodeId === node.id ? "bg-blue-50 ring-1 ring-inset ring-blue-300" : ""}`}
                >
                  <td className="px-4 py-3 font-medium text-gray-900">{node.name}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={node.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-500">{timeAgo(node.lastHeartbeatAt)}</td>
                  <td className="px-4 py-3 text-gray-400 font-mono text-xs">{node.id.slice(0, 8)}…</td>
                  <td className="px-4 py-3 text-right">
                    <button onClick={(e) => handleDelete(e, node.id)} className="text-red-400 hover:text-red-600 text-xs px-2 py-1 rounded hover:bg-red-50 transition-colors">
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

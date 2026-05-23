import { useState } from "react";
import { NodeDashboard } from "./components/NodeDashboard";
import { TaskManager } from "./components/TaskForm";
import { ExecutionMonitor } from "./components/ExecutionMonitor";
import { useStore, type Node } from "./store/store";

type Tab = "nodes" | "tasks" | "logs";

function TabButton({ label, active, onClick, badge }: { label: string; active: boolean; onClick: () => void; badge?: number }) {
  return (
    <button
      onClick={onClick}
      className={`px-4 py-2 text-sm font-medium rounded-t-lg transition-colors ${active ? "bg-white text-blue-700 border-t border-x border-gray-200 -mb-px" : "text-gray-500 hover:text-gray-700"}`}
    >
      {label}
      {badge !== undefined && badge > 0 && (
        <span className="ml-2 px-1.5 py-0.5 text-xs bg-green-100 text-green-700 rounded-full">{badge}</span>
      )}
    </button>
  );
}

function App() {
  const [tab, setTab] = useState<Tab>("nodes");
  const [selectedNode, setSelectedNode] = useState<Node | null>(null);
  const { nodes } = useStore();

  const onlineCount = nodes.filter((n) => n.status === "Online").length;

  const handleSelectNode = (node: Node) => {
    setSelectedNode(node);
    setTab("logs");
  };

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <header className="bg-gradient-to-r from-blue-600 to-blue-800 text-white shadow-lg">
        <div className="max-w-5xl mx-auto px-6 py-5 flex items-center justify-between">
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Swarm</h1>
            <p className="text-blue-200 text-sm mt-0.5">Distributed task orchestrator</p>
          </div>
          <div className="flex items-center gap-2 text-sm bg-blue-700/50 px-3 py-1.5 rounded-full">
            <span className={`w-2 h-2 rounded-full ${onlineCount > 0 ? "bg-green-400 animate-pulse" : "bg-gray-400"}`} />
            <span>{onlineCount} node{onlineCount !== 1 ? "s" : ""} online</span>
          </div>
        </div>
      </header>

      <main className="flex-1 max-w-5xl w-full mx-auto px-6 py-6">
        <div className="flex items-end gap-1 border-b border-gray-200 mb-0">
          <TabButton label="Nodes" active={tab === "nodes"} onClick={() => setTab("nodes")} badge={onlineCount} />
          <TabButton label="Tasks" active={tab === "tasks"} onClick={() => setTab("tasks")} />
          <TabButton
            label={selectedNode ? `Logs — ${selectedNode.name}` : "Logs"}
            active={tab === "logs"}
            onClick={() => setTab("logs")}
          />
        </div>

        <div className="bg-white border border-t-0 border-gray-200 rounded-b-lg shadow-sm p-6">
          {tab === "nodes" && (
            <div>
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-gray-900">Registered Nodes</h2>
                <p className="text-xs text-gray-400">Click a row to stream its logs</p>
              </div>
              <NodeDashboard onSelectNode={handleSelectNode} selectedNodeId={selectedNode?.id} />
            </div>
          )}

          {tab === "tasks" && (
            <div>
              <div className="mb-4">
                <h2 className="text-lg font-semibold text-gray-900">Task Definitions</h2>
                <p className="text-xs text-gray-400 mt-0.5">Create tasks and dispatch them to online nodes via RabbitMQ</p>
              </div>
              <TaskManager />
            </div>
          )}

          {tab === "logs" && (
            <div>
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-lg font-semibold text-gray-900">Live Logs</h2>
                {selectedNode && (
                  <button
                    onClick={() => { setSelectedNode(null); }}
                    className="text-xs text-gray-400 hover:text-gray-600"
                  >
                    Clear selection
                  </button>
                )}
              </div>
              {!selectedNode && nodes.length > 0 && (
                <div className="flex flex-wrap gap-2 mb-4">
                  {nodes.map((n) => (
                    <button
                      key={n.id}
                      onClick={() => setSelectedNode(n)}
                      className="text-sm px-3 py-1.5 rounded-full border border-gray-300 hover:border-blue-400 hover:text-blue-600 transition-colors"
                    >
                      {n.name}
                      <span className={`ml-1.5 w-1.5 h-1.5 inline-block rounded-full ${n.status === "Online" ? "bg-green-500" : "bg-gray-400"}`} />
                    </button>
                  ))}
                </div>
              )}
              <ExecutionMonitor node={selectedNode} />
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

export default App;

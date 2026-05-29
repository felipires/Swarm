import React, { useEffect, useRef, useState } from "react";
import { apiClient } from "../services/api";
import type { Node } from "../store/store";

interface LogEntry {
  id: string;
  timestamp: string;
  level: string;
  message: string;
  nodeId?: string;
}

const LEVEL_ROW: Record<string, string> = {
  Information: "border-l-4 border-blue-400 bg-blue-50 text-blue-900",
  Warning: "border-l-4 border-yellow-400 bg-yellow-50 text-yellow-900",
  Error: "border-l-4 border-red-500 bg-red-50 text-red-900",
  Fatal: "border-l-4 border-red-700 bg-red-100 text-red-900",
};

const LEVEL_BADGE: Record<string, string> = {
  Information: "bg-blue-100 text-blue-700",
  Warning: "bg-yellow-100 text-yellow-700",
  Error: "bg-red-100 text-red-700",
  Fatal: "bg-red-200 text-red-900",
};

interface ExecutionMonitorProps {
  node: Node | null;
}

export const ExecutionMonitor: React.FC<ExecutionMonitorProps> = ({ node }) => {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [connected, setConnected] = useState(false);
  const esRef = useRef<EventSource | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (esRef.current) { esRef.current.close(); esRef.current = null; }
    setLogs([]);
    setConnected(false);

    if (!node) return;

    const url = apiClient.logStreamUrl(node.id);
    const es = new EventSource(url);
    esRef.current = es;

    es.onopen = () => setConnected(true);
    es.onerror = () => setConnected(false);

    es.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        setLogs((prev) => [
          ...prev.slice(-499),
          {
            id: data.id ?? crypto.randomUUID(),
            timestamp: data.timestamp ?? new Date().toISOString(),
            level: data.level ?? "Information",
            message: data.message ?? JSON.stringify(data),
          },
        ]);
      } catch {
        setLogs((prev) => [
          ...prev.slice(-499),
          { id: crypto.randomUUID(), timestamp: new Date().toISOString(), level: "Information", message: event.data },
        ]);
      }
    };

    return () => { es.close(); esRef.current = null; setConnected(false); };
  }, [node?.id]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [logs]);

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3">
        <div className={`flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium ${connected ? "bg-green-100 text-green-800" : "bg-gray-100 text-gray-500"}`}>
          <span className={`w-1.5 h-1.5 rounded-full ${connected ? "bg-green-500 animate-pulse" : "bg-gray-400"}`} />
          {connected ? "Live" : "Disconnected"}
        </div>
        {node && <span className="text-xs text-gray-500">Streaming logs from <span className="font-medium text-gray-700">{node.name}</span></span>}
        {logs.length > 0 && (
          <button onClick={() => setLogs([])} className="ml-auto text-xs text-gray-400 hover:text-gray-600">
            Clear
          </button>
        )}
      </div>

      <div className="h-80 overflow-y-auto bg-gray-950 rounded-lg border border-gray-800 font-mono text-xs">
        {!node ? (
          <div className="h-full flex items-center justify-center text-gray-500">
            Select a node to stream its logs
          </div>
        ) : logs.length === 0 ? (
          <div className="h-full flex items-center justify-center text-gray-500">
            {connected ? "Waiting for logs…" : "Connecting…"}
          </div>
        ) : (
          <div className="p-2 space-y-0.5">
            {logs.map((log) => (
              <div key={log.id} className={`flex gap-2 px-2 py-1 rounded ${LEVEL_ROW[log.level] ?? "bg-gray-900 text-gray-200 border-l-4 border-gray-600"}`}>
                <time className="flex-shrink-0 text-gray-500">{new Date(log.timestamp).toLocaleTimeString()}</time>
                <span className={`flex-shrink-0 px-1.5 rounded text-xs font-bold uppercase ${LEVEL_BADGE[log.level] ?? "bg-gray-700 text-gray-200"}`}>{log.level}</span>
                <span className="break-all">{log.message}</span>
              </div>
            ))}
            <div ref={bottomRef} />
          </div>
        )}
      </div>
    </div>
  );
};

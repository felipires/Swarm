import { useCallback, useEffect, useState } from "react";
import { apiClient } from "../services/api";
import type { Node } from "../store/store";

export type ClusterConnection = "checking" | "connected" | "disconnected";

export interface ClusterPulse {
  connection: ClusterConnection;
  onlineCount: number;
  totalNodes: number;
  alertCount: number;
  refresh: () => void;
}

export function useClusterPulse(): ClusterPulse {
  const [connection, setConnection] = useState<ClusterConnection>("checking");
  const [nodes, setNodes] = useState<Node[]>([]);
  const [alertCount] = useState(0);

  const refresh = useCallback(async () => {
    setConnection("checking");
    try {
      const data = await apiClient.getNodes();
      setNodes(data);
      setConnection("connected");
    } catch {
      setNodes([]);
      setConnection("disconnected");
    }
  }, []);

  useEffect(() => {
    refresh();
    const interval = window.setInterval(refresh, 30_000);
    return () => window.clearInterval(interval);
  }, [refresh]);

  const onlineCount = nodes.filter((n) => n.status === "Online").length;

  return {
    connection,
    onlineCount,
    totalNodes: nodes.length,
    alertCount,
    refresh,
  };
}

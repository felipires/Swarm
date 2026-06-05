import { useQuery } from "@tanstack/react-query";
import { apiClient } from "../services/api";
import { queryKeys } from "../services/queryKeys";
import type { Node } from "../store/store";

export type ClusterConnection = "checking" | "connected" | "disconnected";

const STALE_THRESHOLD_MS = 60_000;

export interface ClusterPulse {
  connection: ClusterConnection;
  nodes: Node[];
  onlineCount: number;
  offlineCount: number;
  staleCount: number;
  totalNodes: number;
  alertCount: number;
  lastUpdated: number | null;
  refresh: () => void;
}

export function isStale(node: Node, now: number): boolean {
  return (
    node.status === "Online" &&
    now - new Date(node.lastHeartbeatAt).getTime() > STALE_THRESHOLD_MS
  );
}

function connectionFrom(
  isError: boolean,
  isFetching: boolean,
  hasData: boolean,
): ClusterConnection {
  if (isError) return "disconnected";
  if (!hasData && isFetching) return "checking";
  return "connected";
}

export function useClusterPulse(): ClusterPulse {
  const query = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
    refetchInterval: 30_000,
  });

  const nodes = query.data ?? [];
  const lastUpdated = query.dataUpdatedAt || null;
  const now = lastUpdated ?? Date.now();

  return {
    connection: connectionFrom(query.isError, query.isFetching, query.data !== undefined),
    nodes,
    onlineCount: nodes.filter((n) => n.status === "Online").length,
    offlineCount: nodes.filter((n) => n.status === "Offline").length,
    staleCount: nodes.filter((n) => isStale(n, now)).length,
    totalNodes: nodes.length,
    alertCount: 0,
    lastUpdated,
    refresh: () => void query.refetch(),
  };
}

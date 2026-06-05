import { useQueries, useQuery } from "@tanstack/react-query";
import { apiClient } from "../services/api";
import { queryKeys } from "../services/queryKeys";
import type { TaskInstance } from "../store/store";

export interface ActivityMetrics {
  /** null = could not load (render as "—"), number = known value. */
  activeRuns: number | null;
  failedToday: number | null;
}

const DAY_MS = 24 * 60 * 60 * 1000;

function isActive(i: TaskInstance): boolean {
  return i.status === "Running" || i.status === "Dispatched";
}

function failedToday(i: TaskInstance, since: number): boolean {
  if (i.status !== "Failed") return false;
  const ts = new Date(i.completedAt ?? i.createdAt).getTime();
  return Number.isFinite(ts) && ts >= since;
}

/** Aggregates task-instance activity. The API exposes instances per task only,
 *  so this fans out over the task list; any failure degrades a metric to null
 *  (the summary band shows "—") rather than throwing. */
export function useActivityMetrics(): ActivityMetrics {
  const tasksQuery = useQuery({
    queryKey: queryKeys.tasks,
    queryFn: () => apiClient.getTasks(),
    refetchInterval: 30_000,
  });

  const tasks = tasksQuery.data ?? [];

  const instanceQueries = useQueries({
    queries: tasks.map((t) => ({
      queryKey: queryKeys.taskInstances(t.id),
      queryFn: () => apiClient.getInstances(t.id),
      refetchInterval: 30_000,
    })),
  });

  if (tasksQuery.isError) {
    return { activeRuns: null, failedToday: null };
  }
  if (tasks.length === 0) {
    return tasksQuery.isSuccess
      ? { activeRuns: 0, failedToday: 0 }
      : { activeRuns: null, failedToday: null };
  }

  const since = Date.now() - DAY_MS;
  let active = 0;
  let failed = 0;
  let anyOk = false;

  for (const q of instanceQueries) {
    if (!q.isSuccess || !q.data) continue;
    anyOk = true;
    for (const inst of q.data) {
      if (isActive(inst)) active++;
      if (failedToday(inst, since)) failed++;
    }
  }

  return anyOk
    ? { activeRuns: active, failedToday: failed }
    : { activeRuns: null, failedToday: null };
}

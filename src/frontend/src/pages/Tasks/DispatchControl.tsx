import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { isStale } from "../../hooks/useClusterPulse";

interface DispatchControlProps {
  taskId: string;
}

type Feedback = { tone: "ok" | "error"; text: string } | null;

export function DispatchControl({ taskId }: DispatchControlProps) {
  const queryClient = useQueryClient();
  const [feedback, setFeedback] = useState<Feedback>(null);

  const nodesQuery = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
  });

  const now = Date.now();
  const onlineNodes = (nodesQuery.data ?? []).filter(
    (n) => n.status === "Online" && !isStale(n, now),
  );

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: queryKeys.taskInstances(taskId) });

  const dispatchOne = useMutation({
    mutationFn: (nodeId: string) => apiClient.dispatchTask(taskId, nodeId),
    onSuccess: (_d, nodeId) => {
      const node = onlineNodes.find((n) => n.id === nodeId);
      setFeedback({ tone: "ok", text: `Dispatched to ${node?.name ?? "node"}.` });
      invalidate();
    },
    onError: () => setFeedback({ tone: "error", text: "Dispatch failed." }),
  });

  const dispatchAll = useMutation({
    mutationFn: () => apiClient.dispatchTaskToAll(taskId),
    onSuccess: (instances) => {
      setFeedback({ tone: "ok", text: `Dispatched to ${instances.length} node(s).` });
      invalidate();
    },
    onError: () => setFeedback({ tone: "error", text: "Dispatch-all failed." }),
  });

  const busy = dispatchOne.isPending || dispatchAll.isPending;

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2">
        <label htmlFor={`node-${taskId}`} className="text-sm text-[var(--swarm-muted)]">
          Dispatch to
        </label>
        <select
          id={`node-${taskId}`}
          disabled={busy || onlineNodes.length === 0}
          defaultValue=""
          onChange={(e) => {
            const id = e.target.value;
            if (id) dispatchOne.mutate(id);
            e.currentTarget.value = "";
          }}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25 disabled:opacity-60"
        >
          <option value="" disabled>
            {onlineNodes.length === 0 ? "No online nodes" : "Select a node…"}
          </option>
          {onlineNodes.map((n) => (
            <option key={n.id} value={n.id}>
              {n.name}
            </option>
          ))}
        </select>

        <span className="text-sm text-[var(--swarm-muted)]">or</span>

        <button
          type="button"
          onClick={() => dispatchAll.mutate()}
          disabled={busy || onlineNodes.length === 0}
          className="inline-flex items-center rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {dispatchAll.isPending ? "Dispatching…" : "All online nodes"}
        </button>
      </div>

      {feedback && (
        <p
          role="status"
          className={`mt-2 text-sm ${
            feedback.tone === "ok"
              ? "text-[var(--swarm-success)]"
              : "text-[var(--swarm-danger)]"
          }`}
        >
          {feedback.text}
        </p>
      )}
    </div>
  );
}

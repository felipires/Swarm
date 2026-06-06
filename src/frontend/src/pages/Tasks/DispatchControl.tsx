import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { TagMapEditor } from "../../components/ui/TagMapEditor";
import { isStale } from "../../hooks/useClusterPulse";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { DispatchRequest, DispatchStrategy } from "../../store/store";
import { STRATEGY_LABEL } from "../Workflows/pipelineGraph";

interface DispatchControlProps {
  taskId: string;
}

type Feedback = { tone: "ok" | "error"; text: string } | null;

const STRATEGIES: DispatchStrategy[] = [
  "AnyOnlineNode",
  "SpecificNode",
  "AllOnlineNodes",
  "TaggedNodes",
];

const fieldClass =
  "rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-2 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25 disabled:opacity-60";

function parseParams(raw: string): { ok: true; value?: Record<string, unknown> } | { ok: false } {
  if (raw.trim() === "") return { ok: true, value: undefined };
  try {
    const parsed = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return { ok: false };
    return { ok: true, value: parsed };
  } catch {
    return { ok: false };
  }
}

export function DispatchControl({ taskId }: DispatchControlProps) {
  const queryClient = useQueryClient();
  const [feedback, setFeedback] = useState<Feedback>(null);

  const [strategy, setStrategy] = useState<DispatchStrategy>("AnyOnlineNode");
  const [nodeId, setNodeId] = useState("");
  const [tags, setTags] = useState<Record<string, string>>({});
  const [paramsRaw, setParamsRaw] = useState("");

  const nodesQuery = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
  });

  const now = Date.now();
  const onlineNodes = (nodesQuery.data ?? []).filter(
    (n) => n.status === "Online" && !isStale(n, now),
  );

  const dispatch = useMutation({
    mutationFn: () => {
      const params = parseParams(paramsRaw);
      const req: DispatchRequest = {
        strategy,
        nodeId: strategy === "SpecificNode" ? nodeId : null,
        targetTags: strategy === "TaggedNodes" ? tags : null,
        runtimeParams: params.ok ? params.value : undefined,
      };
      return apiClient.dispatchTask(taskId, req);
    },
    onSuccess: () => {
      setFeedback({ tone: "ok", text: "Dispatched." });
      queryClient.invalidateQueries({ queryKey: queryKeys.taskInstances(taskId) });
    },
    onError: () =>
      setFeedback({ tone: "error", text: "Dispatch failed. The cluster rejected the request." }),
  });

  const paramsValid = parseParams(paramsRaw).ok;
  const targetValid =
    strategy !== "SpecificNode" || nodeId !== "";
  const tagsValid = strategy !== "TaggedNodes" || Object.keys(tags).length > 0;
  const canDispatch =
    paramsValid && targetValid && tagsValid && !dispatch.isPending && onlineNodes.length > 0;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1">
          <span className="text-xs font-medium text-[var(--swarm-muted)]">Strategy</span>
          <select
            value={strategy}
            onChange={(e) => setStrategy(e.target.value as DispatchStrategy)}
            className={fieldClass}
            aria-label="Dispatch strategy"
          >
            {STRATEGIES.map((s) => (
              <option key={s} value={s}>
                {STRATEGY_LABEL[s]}
              </option>
            ))}
          </select>
        </label>

        {strategy === "SpecificNode" && (
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-[var(--swarm-muted)]">Node</span>
            <select
              value={nodeId}
              onChange={(e) => setNodeId(e.target.value)}
              className={fieldClass}
              aria-label="Target node"
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
          </label>
        )}
      </div>

      {strategy === "TaggedNodes" && (
        <div>
          <span className="mb-1 block text-xs font-medium text-[var(--swarm-muted)]">
            Target tags
          </span>
          <TagMapEditor value={tags} onChange={setTags} />
          {!tagsValid && (
            <p className="mt-1 text-xs text-[var(--swarm-muted)]">Add at least one tag.</p>
          )}
        </div>
      )}

      <div>
        <label htmlFor={`params-${taskId}`} className="mb-1 block text-xs font-medium text-[var(--swarm-muted)]">
          Runtime params <span className="font-normal">(JSON, optional)</span>
        </label>
        <textarea
          id={`params-${taskId}`}
          value={paramsRaw}
          onChange={(e) => setParamsRaw(e.target.value)}
          rows={3}
          spellCheck={false}
          placeholder={'{ "since": "2026-01-01", "limit": 1000 }'}
          aria-invalid={!paramsValid || undefined}
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
        {!paramsValid && (
          <p className="mt-1 text-xs text-[var(--swarm-danger)]">
            Runtime params must be a JSON object.
          </p>
        )}
      </div>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={() => dispatch.mutate()}
          disabled={!canDispatch}
          className="inline-flex items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {dispatch.isPending ? "Dispatching…" : "Dispatch"}
        </button>
        {onlineNodes.length === 0 && (
          <span className="text-sm text-[var(--swarm-muted)]">No online nodes to dispatch to.</span>
        )}
        {feedback && (
          <span
            role="status"
            className={`text-sm ${feedback.tone === "ok" ? "text-[var(--swarm-success)]" : "text-[var(--swarm-danger)]"}`}
          >
            {feedback.text}
          </span>
        )}
      </div>
    </div>
  );
}

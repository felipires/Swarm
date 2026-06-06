import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { MeterBar } from "../../components/ui/MeterBar";
import { Sparkline } from "../../components/ui/Sparkline";
import { StatusPill } from "../../components/ui/StatusPill";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Node } from "../../store/store";
import { formatBytes, formatUptime, HEALTH_TONE, memoryPercent } from "./nodeMetrics";

interface NodeManagePanelProps {
  node: Node;
}

function Metric({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <p className="text-xs text-[var(--swarm-muted)]">{label}</p>
      <div className="mt-0.5 text-sm text-[var(--swarm-ink)]">{children}</div>
    </div>
  );
}

function NodeMetricsSection({ node }: { node: Node }) {
  const metrics = node.latestMetrics ?? null;

  // History powers the sparklines; only fetched while the panel is open.
  const historyQuery = useQuery({
    queryKey: queryKeys.nodeMetrics(node.id),
    queryFn: () => apiClient.getNodeMetrics(node.id),
    refetchInterval: 15_000,
    enabled: Boolean(metrics),
  });

  if (!metrics) {
    return (
      <p className="text-sm text-[var(--swarm-muted)]">
        No live metrics yet. They appear once the node reports a heartbeat sample.
      </p>
    );
  }

  const memPct = memoryPercent(metrics.memoryUsedBytes, metrics.memoryAvailableBytes);
  // History is newest-first; reverse for chronological sparklines.
  const series = [...(historyQuery.data ?? [])].reverse();
  const cpuSeries = series.map((s) => s.cpuPercent);
  const memSeries = series.map((s) => {
    const p = memoryPercent(s.memoryUsedBytes, s.memoryAvailableBytes);
    return p ?? 0;
  });
  const taskSeries = series.map((s) => s.inFlightTasks);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Metric label="Health">
          <StatusPill tone={HEALTH_TONE[metrics.health]} label={metrics.health} />
        </Metric>
        <Metric label="In-flight tasks">
          <span className="font-semibold tabular-nums">{metrics.inFlightTasks}</span>
        </Metric>
        <Metric label="Uptime">
          <span className="tabular-nums">{formatUptime(metrics.uptimeSeconds)}</span>
        </Metric>
        {node.cpuCores != null && (
          <Metric label="Cores">
            <span className="tabular-nums">{node.cpuCores}</span>
          </Metric>
        )}
        {node.totalMemoryBytes != null && (
          <Metric label="Total memory">
            <span className="tabular-nums">{formatBytes(node.totalMemoryBytes)}</span>
          </Metric>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <div className="mb-1 flex items-center justify-between">
            <span className="text-xs text-[var(--swarm-muted)]">CPU</span>
            <span className="text-xs tabular-nums text-[var(--swarm-ink)]">
              {Math.round(metrics.cpuPercent)}%
            </span>
          </div>
          <MeterBar value={metrics.cpuPercent} aria-label="CPU" />
          <div className="mt-2">
            <Sparkline values={cpuSeries} width={200} height={36} max={100} color="var(--swarm-info)" />
          </div>
        </div>
        <div>
          <div className="mb-1 flex items-center justify-between">
            <span className="text-xs text-[var(--swarm-muted)]">Memory</span>
            <span className="text-xs tabular-nums text-[var(--swarm-ink)]">
              {memPct === null ? "—" : `${Math.round(memPct)}%`}
            </span>
          </div>
          {memPct === null ? (
            <span className="text-sm text-[var(--swarm-placeholder)]">—</span>
          ) : (
            <>
              <MeterBar value={memPct} aria-label="Memory" />
              <p className="mt-1 text-xs text-[var(--swarm-muted)]">
                {formatBytes(metrics.memoryUsedBytes)} used
              </p>
            </>
          )}
          <div className="mt-2">
            <Sparkline values={memSeries} width={200} height={36} max={100} color="var(--swarm-primary)" />
          </div>
        </div>
        <div>
          <div className="mb-1 flex items-center justify-between">
            <span className="text-xs text-[var(--swarm-muted)]">In-flight tasks</span>
            <span className="text-xs tabular-nums text-[var(--swarm-ink)]">
              {metrics.inFlightTasks}
            </span>
          </div>
          <Sparkline values={taskSeries} width={200} height={36} color="var(--swarm-success)" />
        </div>
      </div>
    </div>
  );
}

function Capabilities({ node }: { node: Node }) {
  const caps = node.capabilities ?? [];
  if (caps.length === 0) {
    return <p className="text-sm text-[var(--swarm-muted)]">No handlers advertised.</p>;
  }
  return (
    <div className="flex flex-wrap gap-1.5">
      {caps.map((c) => (
        <span
          key={c}
          className="rounded bg-[var(--swarm-surface-raised)] px-1.5 py-0.5 font-mono text-xs text-[var(--swarm-ink)]"
        >
          {c}
        </span>
      ))}
    </div>
  );
}

const inputClass =
  "rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";

function OverlayTags({ node }: { node: Node }) {
  const queryClient = useQueryClient();
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");
  const entries = Object.entries(node.effectiveTags ?? {});

  const invalidate = () => queryClient.invalidateQueries({ queryKey: queryKeys.nodes });

  const add = useMutation({
    mutationFn: () => apiClient.updateNodeTags(node.id, { add: { [key.trim()]: value.trim() } }),
    onSuccess: () => {
      setKey("");
      setValue("");
      invalidate();
    },
  });

  const remove = useMutation({
    mutationFn: (k: string) => apiClient.updateNodeTags(node.id, { remove: [k] }),
    onSuccess: invalidate,
  });

  return (
    <div className="space-y-2">
      {entries.length === 0 ? (
        <p className="text-sm text-[var(--swarm-muted)]">No tags.</p>
      ) : (
        <div className="flex flex-wrap gap-1.5">
          {entries.map(([k, v]) => (
            <span
              key={k}
              className="inline-flex items-center gap-1.5 rounded bg-[var(--swarm-primary-subtle)] py-0.5 pl-1.5 pr-1 font-mono text-xs text-[var(--swarm-ink)]"
            >
              {k}={v}
              <button
                type="button"
                onClick={() => remove.mutate(k)}
                disabled={remove.isPending}
                aria-label={`Remove tag ${k}`}
                className="rounded px-0.5 text-[var(--swarm-muted)] transition-colors hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                style={{ transitionDuration: "var(--swarm-duration)" }}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (key.trim()) add.mutate();
        }}
        className="flex flex-wrap items-center gap-2"
      >
        <input
          value={key}
          onChange={(e) => setKey(e.target.value)}
          placeholder="key"
          className={`${inputClass} w-28 font-mono`}
          aria-label="Tag key"
        />
        <span className="text-[var(--swarm-muted)]">=</span>
        <input
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="value"
          className={`${inputClass} w-28 font-mono`}
          aria-label="Tag value"
        />
        <button
          type="submit"
          disabled={!key.trim() || add.isPending}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Add tag
        </button>
      </form>
      <p className="text-xs text-[var(--swarm-muted)]">
        Adds an overlay tag, applied on the node's next heartbeat. Static (node-local)
        tags win on key conflict and cannot be removed here.
      </p>
    </div>
  );
}

function EnvSecrets({ node }: { node: Node }) {
  const queryClient = useQueryClient();
  const [key, setKey] = useState("");
  const [value, setValue] = useState("");
  const envKey = queryKeys.nodeEnv(node.id);

  const query = useQuery({
    queryKey: envKey,
    queryFn: () => apiClient.getNodeEnvKeys(node.id),
    refetchInterval: 15_000,
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: envKey });

  const setEnv = useMutation({
    mutationFn: () => apiClient.setNodeEnv(node.id, key.trim(), value),
    onSuccess: () => {
      setKey("");
      setValue("");
      invalidate();
    },
  });

  const removeEnv = useMutation({
    mutationFn: (k: string) => apiClient.deleteNodeEnv(node.id, k),
    onSuccess: invalidate,
  });

  const pending = query.data ?? [];

  return (
    <div className="space-y-2">
      {pending.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {pending.map((k) => (
            <span
              key={k}
              className="inline-flex items-center gap-1.5 rounded bg-[var(--swarm-warning-subtle)] py-0.5 pl-2 pr-1 font-mono text-xs text-[var(--swarm-warning)]"
              title="Pending delivery to the node"
            >
              {k}
              <button
                type="button"
                onClick={() => removeEnv.mutate(k)}
                disabled={removeEnv.isPending}
                aria-label={`Cancel pending key ${k}`}
                className="rounded px-0.5 transition-colors hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                style={{ transitionDuration: "var(--swarm-duration)" }}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}

      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (key.trim()) setEnv.mutate();
        }}
        className="flex flex-wrap items-center gap-2"
      >
        <input
          value={key}
          onChange={(e) => setKey(e.target.value)}
          placeholder="API_TOKEN"
          className={`${inputClass} w-36 font-mono`}
          aria-label="Env key"
        />
        <input
          type="password"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="value (encrypted on node)"
          autoComplete="off"
          className={`${inputClass} w-52`}
          aria-label="Env value"
        />
        <button
          type="submit"
          disabled={!key.trim() || setEnv.isPending}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Set key
        </button>
      </form>
      <p className="text-xs text-[var(--swarm-muted)]">
        Values are encrypted on the node and never stored in plaintext by the cluster.
        The list shows keys still pending delivery, not the node's full applied set.
      </p>
    </div>
  );
}

export function NodeManagePanel({ node }: NodeManagePanelProps) {
  return (
    <div className="space-y-5 bg-[var(--swarm-bg)] px-4 py-4">
      <section>
        <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
          Metrics
        </h3>
        <NodeMetricsSection node={node} />
      </section>

      <section>
        <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
          Capabilities
        </h3>
        <Capabilities node={node} />
      </section>

      <div className="grid gap-5 md:grid-cols-2">
        <section>
          <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
            Tags
          </h3>
          <OverlayTags node={node} />
        </section>
        <section>
          <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
            Env secrets
          </h3>
          <EnvSecrets node={node} />
        </section>
      </div>
    </div>
  );
}

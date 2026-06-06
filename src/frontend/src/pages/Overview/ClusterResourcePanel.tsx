import { MeterBar } from "../../components/ui/MeterBar";
import { StatusPill } from "../../components/ui/StatusPill";
import type { Node, NodeHealth } from "../../store/store";
import { HEALTH_TONE, memoryPercent } from "./nodeMetrics";

interface ClusterResourcePanelProps {
  nodes: Node[];
}

const HEALTH_ORDER: NodeHealth[] = ["Healthy", "Degraded", "Unhealthy"];

export function ClusterResourcePanel({ nodes }: ClusterResourcePanelProps) {
  const withMetrics = nodes.filter((n) => n.latestMetrics);

  if (withMetrics.length === 0) {
    return (
      <section className="rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-4">
        <h2 className="text-sm font-semibold text-[var(--swarm-ink)]">Resource usage</h2>
        <p className="mt-2 text-sm text-[var(--swarm-muted)]">
          No live metrics yet. Nodes report CPU, memory, and load on each heartbeat.
        </p>
      </section>
    );
  }

  const avgCpu =
    withMetrics.reduce((sum, n) => sum + (n.latestMetrics?.cpuPercent ?? 0), 0) /
    withMetrics.length;

  const memPcts = withMetrics
    .map((n) => memoryPercent(n.latestMetrics!.memoryUsedBytes, n.latestMetrics!.memoryAvailableBytes))
    .filter((p): p is number => p !== null);
  const avgMem = memPcts.length > 0 ? memPcts.reduce((a, b) => a + b, 0) / memPcts.length : null;

  const inFlight = withMetrics.reduce((sum, n) => sum + (n.latestMetrics?.inFlightTasks ?? 0), 0);

  const healthCounts = HEALTH_ORDER.map((h) => ({
    health: h,
    count: withMetrics.filter((n) => n.latestMetrics?.health === h).length,
  })).filter((x) => x.count > 0);

  return (
    <section className="rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-4">
      <div className="mb-3 flex items-baseline justify-between">
        <h2 className="text-sm font-semibold text-[var(--swarm-ink)]">Resource usage</h2>
        <span className="text-xs text-[var(--swarm-muted)]">
          {withMetrics.length} of {nodes.length} reporting
        </span>
      </div>

      <dl className="space-y-3">
        <div className="flex items-center gap-3">
          <dt className="w-28 shrink-0 text-sm text-[var(--swarm-muted)]">Avg CPU</dt>
          <dd className="flex-1">
            <MeterBar value={avgCpu} showValue aria-label="Average CPU across reporting nodes" />
          </dd>
        </div>
        <div className="flex items-center gap-3">
          <dt className="w-28 shrink-0 text-sm text-[var(--swarm-muted)]">Avg memory</dt>
          <dd className="flex-1">
            {avgMem === null ? (
              <span className="text-sm text-[var(--swarm-placeholder)]">—</span>
            ) : (
              <MeterBar value={avgMem} showValue aria-label="Average memory across reporting nodes" />
            )}
          </dd>
        </div>
        <div className="flex items-center gap-3">
          <dt className="w-28 shrink-0 text-sm text-[var(--swarm-muted)]">In-flight tasks</dt>
          <dd className="text-sm font-semibold tabular-nums text-[var(--swarm-ink)]">{inFlight}</dd>
        </div>
        <div className="flex items-center gap-3">
          <dt className="w-28 shrink-0 text-sm text-[var(--swarm-muted)]">Node health</dt>
          <dd className="flex flex-wrap items-center gap-1.5">
            {healthCounts.map(({ health, count }) => (
              <StatusPill key={health} tone={HEALTH_TONE[health]} label={`${count} ${health}`} />
            ))}
          </dd>
        </div>
      </dl>
    </section>
  );
}

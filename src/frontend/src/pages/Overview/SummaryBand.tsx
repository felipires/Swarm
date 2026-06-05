type Tone = "ink" | "success" | "warning" | "danger" | "info";

const TONE_COLOR: Record<Tone, string> = {
  ink: "var(--swarm-ink)",
  success: "var(--swarm-success)",
  warning: "var(--swarm-warning)",
  danger: "var(--swarm-danger)",
  info: "var(--swarm-info)",
};

interface Metric {
  label: string;
  value: number | null;
  tone: Tone;
  /** When the value is 0, fall back to muted ink instead of an alarm color. */
  quietWhenZero?: boolean;
}

function MetricCell({ metric, loading }: { metric: Metric; loading: boolean }) {
  const { label, value, tone, quietWhenZero } = metric;
  const isUnknown = value === null;
  const color =
    isUnknown || (quietWhenZero && value === 0)
      ? "var(--swarm-muted)"
      : TONE_COLOR[tone];

  return (
    <div className="flex flex-col gap-1 px-4 py-3">
      <dd className="text-2xl font-semibold tabular-nums" style={{ color }}>
        {loading ? (
          <span className="inline-block h-7 w-10 animate-pulse rounded bg-[var(--swarm-surface-raised)] align-middle motion-reduce:animate-none" />
        ) : isUnknown ? (
          "—"
        ) : (
          value
        )}
      </dd>
      <dt className="text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
        {label}
      </dt>
    </div>
  );
}

interface SummaryBandProps {
  totalNodes: number;
  onlineCount: number;
  offlineCount: number;
  staleCount: number;
  activeRuns: number | null;
  failedToday: number | null;
  loading: boolean;
}

export function SummaryBand({
  totalNodes,
  onlineCount,
  offlineCount,
  staleCount,
  activeRuns,
  failedToday,
  loading,
}: SummaryBandProps) {
  const metrics: Metric[] = [
    { label: "Total nodes", value: totalNodes, tone: "ink" },
    { label: "Online", value: onlineCount, tone: "success", quietWhenZero: true },
    { label: "Offline", value: offlineCount, tone: "danger", quietWhenZero: true },
    { label: "Stale", value: staleCount, tone: "warning", quietWhenZero: true },
    { label: "Active runs", value: activeRuns, tone: "info", quietWhenZero: true },
    { label: "Failed today", value: failedToday, tone: "danger", quietWhenZero: true },
  ];

  return (
    <dl className="grid grid-cols-3 divide-x divide-y divide-[var(--swarm-border)] overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] sm:grid-cols-6 sm:divide-y-0">
      {metrics.map((m) => (
        <MetricCell key={m.label} metric={m} loading={loading} />
      ))}
    </dl>
  );
}

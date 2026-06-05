import type { ClusterConnection } from "../../hooks/useClusterPulse";
import { IconAlert, IconCluster, IconNodes, IconSearch } from "./icons";

interface StatusStripProps {
  alertCount: number;
  connection: ClusterConnection;
  onlineCount: number;
  totalNodes: number;
  searchQuery: string;
  onSearchChange: (value: string) => void;
  onAlertsClick: () => void;
}

function connectionLabel(connection: ClusterConnection): string {
  if (connection === "checking") return "Checking cluster";
  if (connection === "connected") return "Cluster connected";
  return "Cluster unreachable";
}

function ConnectionIndicator({ connection }: { connection: ClusterConnection }) {
  const tone =
    connection === "connected"
      ? "bg-[var(--swarm-success-subtle)] text-[var(--swarm-success)]"
      : connection === "checking"
        ? "bg-[var(--swarm-surface-raised)] text-[var(--swarm-muted)]"
        : "bg-[var(--swarm-danger-subtle)] text-[var(--swarm-danger)]";

  return (
    <span className={`inline-flex items-center gap-2 rounded-md px-2.5 py-1 text-xs font-medium ${tone}`}>
      <IconCluster width={14} height={14} />
      <span className="hidden sm:inline">{connectionLabel(connection)}</span>
      <span className="sr-only sm:hidden">{connectionLabel(connection)}</span>
    </span>
  );
}

export function StatusStrip({
  alertCount,
  connection,
  onlineCount,
  totalNodes,
  searchQuery,
  onSearchChange,
  onAlertsClick,
}: StatusStripProps) {
  const hasAlerts = alertCount > 0;

  return (
    <header
      className="sticky top-0 z-[var(--z-sticky)] flex flex-wrap items-center gap-3 border-b border-[var(--swarm-border)] bg-[var(--swarm-chrome)] px-4 py-2.5"
      role="banner"
    >
      <button
        type="button"
        onClick={onAlertsClick}
        className={[
          "inline-flex items-center gap-2 rounded-md px-2.5 py-1.5 text-xs font-medium transition-colors",
          "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]",
          hasAlerts
            ? "bg-[var(--swarm-warning-subtle)] text-[var(--swarm-warning)] hover:opacity-90"
            : "bg-[var(--swarm-surface-raised)] text-[var(--swarm-muted)] hover:text-[var(--swarm-ink)]",
        ].join(" ")}
        style={{ transitionDuration: "var(--swarm-duration)" }}
        aria-label={hasAlerts ? `${alertCount} alerts, open observability` : "No alerts, open observability"}
      >
        <IconAlert width={14} height={14} />
        <span>{hasAlerts ? `${alertCount} alert${alertCount === 1 ? "" : "s"}` : "No alerts"}</span>
      </button>

      <div className="relative min-w-[12rem] flex-1 max-w-md">
        <label htmlFor="global-search" className="sr-only">
          Search nodes, runs, and pipelines
        </label>
        <IconSearch
          width={15}
          height={15}
          className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--swarm-muted)]"
        />
        <input
          id="global-search"
          type="search"
          value={searchQuery}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Search nodes, runs, pipelines…"
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] py-1.5 pl-9 pr-3 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
      </div>

      <div className="ml-auto flex flex-wrap items-center gap-2">
        <ConnectionIndicator connection={connection} />

        <span className="inline-flex items-center gap-2 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1 text-xs text-[var(--swarm-ink)]">
          <IconNodes width={14} height={14} className="text-[var(--swarm-muted)]" />
          <span>
            <span className="font-semibold tabular-nums">{onlineCount}</span>
            <span className="text-[var(--swarm-muted)]"> / {totalNodes} online</span>
          </span>
        </span>
      </div>
    </header>
  );
}

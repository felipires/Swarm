import type { NavRoute } from "../../types/navigation";
import { PRIMARY_NAV, SETTINGS_NAV } from "../../types/navigation";

const PLACEHOLDER_COPY: Record<NavRoute, { title: string; body: string; action?: string }> = {
  overview: {
    title: "Cluster overview",
    body: "Connect a node to see cluster health, active runs, and recent failures in one place.",
    action: "Start a node worker and register it with the cluster API.",
  },
  workflows: {
    title: "Workflows",
    body: "Pipeline authoring opens here. Define steps, schedules, and dependencies on a canvas.",
    action: "Workflow editor and run history will mount in this frame.",
  },
  observability: {
    title: "Observability",
    body: "Inspect logs, metrics, and execution history without leaving the shell.",
    action: "Select a node or run to stream signals into embedded panels.",
  },
  settings: {
    title: "Settings",
    body: "Configure cluster connection, API credentials, and notification rules.",
    action: "Connection settings and environment profiles will live here.",
  },
};

function navMeta(route: NavRoute) {
  return [...PRIMARY_NAV, SETTINGS_NAV].find((item) => item.id === route);
}

interface PlaceholderViewProps {
  route: NavRoute;
  connection: "checking" | "connected" | "disconnected";
}

export function PlaceholderView({ route, connection }: PlaceholderViewProps) {
  const copy = PLACEHOLDER_COPY[route];
  const meta = navMeta(route);

  return (
    <div className="mx-auto flex h-full max-w-3xl flex-col justify-center px-6 py-12">
      <p className="mb-2 text-sm font-medium text-[var(--swarm-primary)]">{meta?.label}</p>
      <h1
        className="mb-3 text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]"
        style={{ textWrap: "balance" }}
      >
        {copy.title}
      </h1>
      <p className="mb-6 max-w-prose text-base leading-relaxed text-[var(--swarm-muted)]" style={{ textWrap: "pretty" }}>
        {copy.body}
      </p>

      {connection === "disconnected" && route === "overview" && (
        <div
          role="alert"
          className="mb-6 rounded-md border border-[var(--swarm-danger)]/30 bg-[var(--swarm-danger-subtle)] px-4 py-3 text-sm text-[var(--swarm-danger)]"
        >
          Cannot reach the cluster API. Check that Swarm.Cluster is running on port 5001.
        </div>
      )}

      {connection === "checking" && route === "overview" && (
        <div className="mb-6 space-y-2" aria-busy="true" aria-label="Loading cluster status">
          <div className="h-3 w-48 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
          <div className="h-3 w-64 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
        </div>
      )}

      <p className="text-sm text-[var(--swarm-ink)]">{copy.action}</p>
    </div>
  );
}

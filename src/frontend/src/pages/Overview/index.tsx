import { useMutation, useQueryClient } from "@tanstack/react-query";
import { IconRefresh } from "../../components/shell/icons";
import { useActivityMetrics } from "../../hooks/useActivityMetrics";
import type { ClusterPulse } from "../../hooks/useClusterPulse";
import { useTicker } from "../../hooks/useTicker";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Node } from "../../store/store";
import { absoluteTime } from "../../utils/time";
import { NodeTable } from "./NodeTable";
import { SummaryBand } from "./SummaryBand";

interface OverviewPageProps {
  pulse: ClusterPulse;
}

const TableSkeleton = () => (
  <div
    className="space-y-2 rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-3"
    aria-busy="true"
    aria-label="Loading nodes"
  >
    {Array.from({ length: 4 }).map((_, i) => (
      <div
        key={i}
        className="h-9 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none"
      />
    ))}
  </div>
);

const DisconnectedState = () => (
  <div
    role="alert"
    className="rounded-lg border border-[var(--swarm-danger)]/30 bg-[var(--swarm-danger-subtle)] px-4 py-3 text-sm text-[var(--swarm-danger)]"
  >
    Cannot reach the cluster API. Check that Swarm.Cluster is running on port 5001.
  </div>
);

const EmptyState = () => (
  <div className="rounded-lg border border-dashed border-[var(--swarm-border-strong)] bg-[var(--swarm-surface)] px-6 py-10 text-center">
    <p className="text-sm font-medium text-[var(--swarm-ink)]">No nodes registered</p>
    <p className="mx-auto mt-1 max-w-md text-sm text-[var(--swarm-muted)]">
      Start a worker and point it at this cluster. It registers automatically and
      appears here on its first heartbeat.
    </p>
    <code className="mt-4 inline-block rounded bg-[var(--swarm-surface-raised)] px-3 py-1.5 font-mono text-xs text-[var(--swarm-ink)]">
      dotnet run --project src/Swarm.Node
    </code>
  </div>
);

export const OverviewPage = ({ pulse }: OverviewPageProps) => {
  const now = useTicker(5_000);
  const activity = useActivityMetrics();
  const queryClient = useQueryClient();

  const deleteNode = useMutation({
    mutationFn: (node: Node) => apiClient.deleteNode(node.id),
    onMutate: async (node: Node) => {
      await queryClient.cancelQueries({ queryKey: queryKeys.nodes });
      const previous = queryClient.getQueryData<Node[]>(queryKeys.nodes);
      queryClient.setQueryData<Node[]>(queryKeys.nodes, (old) =>
        (old ?? []).filter((n) => n.id !== node.id),
      );
      return { previous };
    },
    onError: (_err, _node, ctx) => {
      if (ctx?.previous) queryClient.setQueryData(queryKeys.nodes, ctx.previous);
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.nodes });
    },
  });

  const handleDelete = (node: Node) => deleteNode.mutateAsync(node);

  const nodes = pulse.nodes;
  const initialLoad = pulse.connection === "checking" && pulse.lastUpdated === null;

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 px-6 py-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]">
            Cluster overview
          </h1>
          <p className="mt-1 text-sm text-[var(--swarm-muted)]">
            {pulse.lastUpdated
              ? `Updated ${absoluteTime(new Date(pulse.lastUpdated).toISOString())}`
              : "Connecting to cluster…"}
          </p>
        </div>
        <button
          type="button"
          onClick={pulse.refresh}
          className="inline-flex items-center gap-2 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <IconRefresh
            width={15}
            height={15}
            className={pulse.connection === "checking" ? "animate-spin motion-reduce:animate-none" : undefined}
          />
          Refresh
        </button>
      </header>

      <SummaryBand
        totalNodes={nodes.length}
        onlineCount={nodes.filter((n) => n.status === "Online").length}
        offlineCount={nodes.filter((n) => n.status === "Offline").length}
        staleCount={pulse.staleCount}
        activeRuns={activity.activeRuns}
        failedToday={activity.failedToday}
        loading={initialLoad}
      />

      <section aria-label="Registered nodes">
        {pulse.connection === "disconnected" ? (
          <DisconnectedState />
        ) : initialLoad ? (
          <TableSkeleton />
        ) : nodes.length === 0 ? (
          <EmptyState />
        ) : (
          <NodeTable nodes={nodes} now={now} onDelete={handleDelete} />
        )}
      </section>
    </div>
  );
};

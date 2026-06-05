import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { StatusPill } from "../../components/ui/StatusPill";
import { isStale } from "../../hooks/useClusterPulse";
import { useLogStream } from "../../hooks/useLogStream";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Node } from "../../store/store";
import { LogStreamPanel } from "./LogStreamPanel";
import { LogToolbar } from "./LogToolbar";
import type { LevelFilter } from "./logLevels";

function nodeTone(node: Node, now: number) {
  if (node.status === "Offline") return { tone: "danger" as const, label: "Offline" };
  return isStale(node, now)
    ? { tone: "warning" as const, label: "Stale" }
    : { tone: "success" as const, label: "Online" };
}

export const ObservabilityPage = () => {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [filter, setFilter] = useState<LevelFilter>("All");
  const [query, setQuery] = useState("");
  const [live, setLive] = useState(true);
  const [autoScroll, setAutoScroll] = useState(true);

  const nodesQuery = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
    refetchInterval: 30_000,
  });
  const nodes = nodesQuery.data ?? [];
  const now = Date.now();

  // Default to the first node once the list loads.
  useEffect(() => {
    if (!selectedNodeId && nodes.length > 0) {
      setSelectedNodeId(nodes[0].id);
    }
  }, [nodes, selectedNodeId]);

  const { entries, state, clear } = useLogStream(selectedNodeId, live);

  return (
    <div className="flex h-full flex-col px-6 py-6">
      <header className="mb-4">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]">
          Observability
        </h1>
        <p className="mt-1 text-sm text-[var(--swarm-muted)]">
          Stream logs from a node in real time. Filter by level and search messages.
        </p>
      </header>

      <div className="grid min-h-0 flex-1 grid-cols-1 gap-4 lg:grid-cols-[16rem_1fr]">
        <aside className="hidden min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] lg:flex">
          <div className="border-b border-[var(--swarm-border)] px-3 py-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
            Nodes
          </div>
          <ul className="min-h-0 flex-1 overflow-y-auto p-1">
            {nodes.length === 0 ? (
              <li className="px-2 py-3 text-sm text-[var(--swarm-muted)]">No nodes registered.</li>
            ) : (
              nodes.map((node) => {
                const { tone, label } = nodeTone(node, now);
                const isSelected = node.id === selectedNodeId;
                return (
                  <li key={node.id}>
                    <button
                      type="button"
                      onClick={() => setSelectedNodeId(node.id)}
                      aria-current={isSelected ? "true" : undefined}
                      className={`flex w-full items-center justify-between gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] ${
                        isSelected
                          ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
                          : "text-[var(--swarm-muted)] hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)]"
                      }`}
                      style={{ transitionDuration: "var(--swarm-duration)" }}
                    >
                      <span className="truncate">{node.name}</span>
                      <StatusPill tone={tone} label={label} pulsing={label === "Stale"} />
                    </button>
                  </li>
                );
              })
            )}
          </ul>
        </aside>

        <section className="flex min-h-0 flex-col overflow-hidden rounded-lg border border-[var(--swarm-border)]">
          <LogToolbar
            nodes={nodes}
            selectedNodeId={selectedNodeId}
            onSelectNode={setSelectedNodeId}
            filter={filter}
            onFilterChange={setFilter}
            query={query}
            onQueryChange={setQuery}
            live={live}
            onLiveChange={setLive}
            onClear={clear}
            state={state}
            count={entries.length}
          />
          <div className="min-h-0 flex-1">
            {selectedNodeId ? (
              <LogStreamPanel
                entries={entries}
                filter={filter}
                query={query}
                autoScroll={autoScroll}
                onAutoScrollChange={setAutoScroll}
              />
            ) : (
              <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
                Select a node to stream its logs.
              </div>
            )}
          </div>
        </section>
      </div>
    </div>
  );
};

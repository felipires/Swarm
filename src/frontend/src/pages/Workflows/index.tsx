import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { IconSearch } from "../../components/shell/icons";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { PipelineRow } from "./PipelineRow";

const ListSkeleton = () => (
  <div
    className="divide-y divide-[var(--swarm-border)] rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]"
    aria-busy="true"
    aria-label="Loading pipelines"
  >
    {Array.from({ length: 4 }).map((_, i) => (
      <div key={i} className="px-4 py-3">
        <div className="h-5 w-48 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
      </div>
    ))}
  </div>
);

const EmptyState = () => (
  <div className="rounded-lg border border-dashed border-[var(--swarm-border-strong)] bg-[var(--swarm-surface)] px-6 py-10 text-center">
    <p className="text-sm font-medium text-[var(--swarm-ink)]">
      No pipelines defined
    </p>
    <p className="mx-auto mt-1 max-w-md text-sm text-[var(--swarm-muted)]">
      A pipeline chains task steps with dependencies, dispatch strategies, and
      an optional cron schedule. Define one through the cluster API to see it
      here.
    </p>
  </div>
);

export const WorkflowsPage = () => {
  const navigate = useNavigate();
  const [filter, setFilter] = useState("");
  const [showDeleted, setShowDeleted] = useState(false);

  const query = useQuery({
    queryKey: [...queryKeys.pipelines, { includeDeleted: showDeleted }],
    queryFn: () => apiClient.getPipelines(showDeleted),
    refetchInterval: 30_000,
  });

  const pipelines = useMemo(() => {
    const all = query.data ?? [];
    const q = filter.trim().toLowerCase();
    const matched = q
      ? all.filter(
          (p) =>
            p.name.toLowerCase().includes(q) ||
            p.description?.toLowerCase().includes(q),
        )
      : all;
    return [...matched].sort(
      (a, b) =>
        new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime(),
    );
  }, [query.data, filter]);

  const total = query.data?.length ?? 0;

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-6 px-6 py-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]">
            Workflows
          </h1>
          <p className="mt-1 text-sm text-[var(--swarm-muted)]">
            Pipeline definitions, their schedules, and run history.
          </p>
        </div>

        <div className="flex items-center gap-2">
          {total > 0 && (
            <div className="relative w-full max-w-xs">
              <label htmlFor="pipeline-filter" className="sr-only">
                Filter pipelines
              </label>
              <IconSearch
                width={15}
                height={15}
                className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--swarm-muted)]"
              />
              <input
                id="pipeline-filter"
                type="search"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                placeholder="Filter by name…"
                className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] py-1.5 pl-9 pr-3 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
              />
            </div>
          )}
          <label className="flex shrink-0 items-center gap-1.5 text-sm text-[var(--swarm-muted)]">
            <input
              type="checkbox"
              checked={showDeleted}
              onChange={(e) => setShowDeleted(e.target.checked)}
              className="h-4 w-4 accent-[var(--swarm-primary)]"
            />
            Show deleted
          </label>
          <button
            type="button"
            onClick={() => navigate("/workflows/new")}
            className="inline-flex shrink-0 items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            New pipeline
          </button>
        </div>
      </header>

      {query.isLoading ? (
        <ListSkeleton />
      ) : query.isError ? (
        <div
          role="alert"
          className="rounded-lg border border-[var(--swarm-danger)]/30 bg-[var(--swarm-danger-subtle)] px-4 py-3 text-sm text-[var(--swarm-danger)]"
        >
          Could not load pipelines. Check that the cluster API is reachable.
        </div>
      ) : total === 0 ? (
        <EmptyState />
      ) : pipelines.length === 0 ? (
        <p className="text-sm text-[var(--swarm-muted)]">
          No pipelines match “{filter}”.
        </p>
      ) : (
        <div className="overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]">
          {pipelines.map((p) => (
            <PipelineRow key={p.id} pipeline={p} />
          ))}
        </div>
      )}
    </div>
  );
};

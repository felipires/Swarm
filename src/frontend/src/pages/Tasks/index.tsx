import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { IconSearch } from "../../components/shell/icons";
import { useTicker } from "../../hooks/useTicker";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { CreateTaskForm } from "./CreateTaskForm";
import { TaskRow } from "./TaskRow";

const ListSkeleton = () => (
  <div
    className="divide-y divide-[var(--swarm-border)] rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]"
    aria-busy="true"
    aria-label="Loading tasks"
  >
    {Array.from({ length: 4 }).map((_, i) => (
      <div key={i} className="px-4 py-3">
        <div className="h-5 w-48 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
      </div>
    ))}
  </div>
);

const EmptyState = ({ onCreate }: { onCreate: () => void }) => (
  <div className="rounded-lg border border-dashed border-[var(--swarm-border-strong)] bg-[var(--swarm-surface)] px-6 py-10 text-center">
    <p className="text-sm font-medium text-[var(--swarm-ink)]">
      No tasks defined
    </p>
    <p className="mx-auto mt-1 max-w-md text-sm text-[var(--swarm-muted)]">
      A task is a single unit of work (an HTTP call, a SQL statement, a webhook)
      that a node executes. Define one, then dispatch it to a node or compose it
      into a pipeline.
    </p>
    <button
      type="button"
      onClick={onCreate}
      className="mt-4 inline-flex items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
      style={{ transitionDuration: "var(--swarm-duration)" }}
    >
      Create task
    </button>
  </div>
);

export const TasksPage = () => {
  const now = useTicker(5_000);
  const [filter, setFilter] = useState("");
  const [creating, setCreating] = useState(false);
  const [showDeleted, setShowDeleted] = useState(false);

  const query = useQuery({
    queryKey: [...queryKeys.tasks, { includeDeleted: showDeleted }],
    queryFn: () => apiClient.getTasks(showDeleted),
    refetchInterval: 30_000,
  });

  const tasks = useMemo(() => {
    const all = query.data ?? [];
    const q = filter.trim().toLowerCase();
    const matched = q
      ? all.filter(
          (t) =>
            t.name.toLowerCase().includes(q) ||
            t.description?.toLowerCase().includes(q),
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
            Tasks
          </h1>
          <p className="mt-1 text-sm text-[var(--swarm-muted)]">
            Task definitions, dispatch, and execution history.
          </p>
        </div>

        <div className="flex items-center gap-2">
          {total > 0 && (
            <div className="relative w-full max-w-xs">
              <label htmlFor="task-filter" className="sr-only">
                Filter tasks
              </label>
              <IconSearch
                width={15}
                height={15}
                className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--swarm-muted)]"
              />
              <input
                id="task-filter"
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
            onClick={() => setCreating((c) => !c)}
            aria-expanded={creating}
            className="inline-flex shrink-0 items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            New task
          </button>
        </div>
      </header>

      {creating && <CreateTaskForm onClose={() => setCreating(false)} />}

      {query.isLoading ? (
        <ListSkeleton />
      ) : query.isError ? (
        <div
          role="alert"
          className="rounded-lg border border-[var(--swarm-danger)]/30 bg-[var(--swarm-danger-subtle)] px-4 py-3 text-sm text-[var(--swarm-danger)]"
        >
          Could not load tasks. Check that the cluster API is reachable.
        </div>
      ) : total === 0 ? (
        !creating && <EmptyState onCreate={() => setCreating(true)} />
      ) : tasks.length === 0 ? (
        <p className="text-sm text-[var(--swarm-muted)]">
          No tasks match “{filter}”.
        </p>
      ) : (
        <div className="overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]">
          {tasks.map((task) => (
            <TaskRow key={task.id} task={task} now={now} />
          ))}
        </div>
      )}
    </div>
  );
};

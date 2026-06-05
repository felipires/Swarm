import { useInfiniteQuery } from "@tanstack/react-query";
import { StatusPill } from "../../components/ui/StatusPill";
import { useTicker } from "../../hooks/useTicker";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { PipelineRun } from "../../store/store";
import { absoluteTime, duration, relativeTime } from "../../utils/time";
import { isRunning, RUN_TONE } from "./runStatus";

interface RunHistoryProps {
  pipelineId: string;
}

export function RunHistory({ pipelineId }: RunHistoryProps) {
  const now = useTicker(5_000);
  const query = useInfiniteQuery({
    queryKey: queryKeys.pipelineRuns(pipelineId),
    queryFn: ({ pageParam }) => apiClient.getPipelineRuns(pipelineId, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : undefined),
  });

  if (query.isLoading) {
    return (
      <div className="space-y-1.5" aria-busy="true" aria-label="Loading run history">
        {Array.from({ length: 3 }).map((_, i) => (
          <div
            key={i}
            className="h-8 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none"
          />
        ))}
      </div>
    );
  }

  if (query.isError) {
    return (
      <p className="text-sm text-[var(--swarm-danger)]" role="alert">
        Could not load run history.
      </p>
    );
  }

  const runs: PipelineRun[] = query.data?.pages.flatMap((p) => p.items) ?? [];

  if (runs.length === 0) {
    return <p className="text-sm text-[var(--swarm-muted)]">No runs yet.</p>;
  }

  return (
    <div>
      <ul className="divide-y divide-[var(--swarm-border)]">
        {runs.map((run) => (
          <li
            key={run.id}
            className="flex flex-wrap items-center gap-x-4 gap-y-1 py-2 text-sm"
          >
            <span
              className="tabular-nums text-[var(--swarm-muted)]"
              title={absoluteTime(run.startedAt)}
            >
              {relativeTime(run.startedAt, now)}
            </span>
            <span className="tabular-nums text-[var(--swarm-muted)]">
              {duration(run.startedAt, run.completedAt, now)}
            </span>
            <StatusPill
              tone={RUN_TONE[run.status]}
              label={run.status}
              pulsing={isRunning(run.status)}
            />
            {run.errorMessage && (
              <span
                className="min-w-0 flex-1 truncate font-mono text-xs text-[var(--swarm-danger)]"
                title={run.errorMessage}
              >
                {run.errorMessage}
              </span>
            )}
          </li>
        ))}
      </ul>

      {query.hasNextPage && (
        <button
          type="button"
          onClick={() => query.fetchNextPage()}
          disabled={query.isFetchingNextPage}
          className="mt-2 rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {query.isFetchingNextPage ? "Loading…" : "Show older runs"}
        </button>
      )}
    </div>
  );
}

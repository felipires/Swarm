import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { LogQueryParams, LogRecord } from "../../store/store";
import { styleForLevel } from "./logLevels";

const timeFmt = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});

function logTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "--:--:--" : timeFmt.format(d);
}

interface LogResultsProps {
  params: LogQueryParams;
  /** Poll interval in ms; 0/undefined disables auto-refresh. */
  refetchMs?: number;
  /** Click handler for a tag chip — lets the caller refine the query. */
  onAddFacet?: (key: string, value: string) => void;
  emptyHint?: string;
}

/**
 * Paginated log list backed by `GET /api/logs` (cursor, newest-first, load-older).
 * Shared by the global search page and the pipeline run/step log panels.
 */
export function LogResults({ params, refetchMs, onAddFacet, emptyHint }: LogResultsProps) {
  const query = useInfiniteQuery({
    queryKey: queryKeys.logs(params),
    queryFn: ({ pageParam }) => apiClient.getLogs(params, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : undefined),
    refetchInterval: refetchMs && refetchMs > 0 ? refetchMs : false,
  });

  const rows: LogRecord[] = query.data?.pages.flatMap((p) => p.items) ?? [];

  if (query.isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
        Loading logs…
      </div>
    );
  }

  if (query.isError) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-danger)]">
        Failed to load logs.
      </div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
        {emptyHint ?? "No logs match the current filters."}
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-y-auto bg-[var(--swarm-bg)]" role="log" aria-label="Log results">
      <ul className="divide-y divide-[var(--swarm-border)]/60">
        {rows.map((row) => (
          <LogRow key={row.id} row={row} onAddFacet={onAddFacet} />
        ))}
      </ul>
      <div className="p-2">
        {query.hasNextPage ? (
          <button
            type="button"
            onClick={() => query.fetchNextPage()}
            disabled={query.isFetchingNextPage}
            className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-xs font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] disabled:opacity-60"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            {query.isFetchingNextPage ? "Loading…" : "Load older"}
          </button>
        ) : (
          <p className="py-1 text-center text-xs text-[var(--swarm-muted)]">End of results</p>
        )}
      </div>
    </div>
  );
}

function LogRow({
  row,
  onAddFacet,
}: {
  row: LogRecord;
  onAddFacet?: (key: string, value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const style = styleForLevel(row.level);
  const message = row.message ?? row.messageTemplate;
  const tagEntries = Object.entries(row.tags ?? {});

  return (
    <li className="px-3 py-1 font-mono text-xs">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-start gap-3 text-left"
      >
        <span className="shrink-0 tabular-nums text-[var(--swarm-muted)]">{logTime(row.timestamp)}</span>
        <span
          className="w-8 shrink-0 font-medium"
          style={{ color: style.color, fontWeight: style.bold ? 700 : 600 }}
        >
          {style.abbr}
        </span>
        <span
          className={`min-w-0 flex-1 ${open ? "whitespace-pre-wrap break-words" : "truncate"}`}
          style={{ color: style.color, fontWeight: style.bold ? 600 : 400 }}
          title={message}
        >
          {message}
        </span>
      </button>

      {open && (
        <div className="mt-1 space-y-2 pl-[4.75rem]">
          {tagEntries.length > 0 && (
            <div className="flex flex-wrap gap-1">
              {tagEntries.map(([key, value]) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => onAddFacet?.(key, value)}
                  disabled={!onAddFacet}
                  className="rounded bg-[var(--swarm-surface-raised)] px-1.5 py-0.5 text-[10px] text-[var(--swarm-muted)] transition-colors enabled:hover:text-[var(--swarm-ink)]"
                  title={onAddFacet ? `Filter by ${key}:${value}` : undefined}
                >
                  <span className="text-[var(--swarm-primary)]">{key}</span>:{truncate(value)}
                </button>
              ))}
            </div>
          )}
          {row.exception && (
            <pre className="overflow-x-auto rounded border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-2 text-[10px] text-[var(--swarm-danger)]">
              {row.exception}
            </pre>
          )}
        </div>
      )}
    </li>
  );
}

function truncate(value: string): string {
  return value.length > 40 ? `${value.slice(0, 37)}…` : value;
}

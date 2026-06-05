import type { StreamState } from "../../hooks/useLogStream";
import type { Node } from "../../store/store";
import { IconSearch } from "../../components/shell/icons";
import { LEVEL_FILTERS, type LevelFilter } from "./logLevels";

interface LogToolbarProps {
  nodes: Node[];
  selectedNodeId: string | null;
  onSelectNode: (id: string | null) => void;
  filter: LevelFilter;
  onFilterChange: (filter: LevelFilter) => void;
  query: string;
  onQueryChange: (q: string) => void;
  live: boolean;
  onLiveChange: (live: boolean) => void;
  onClear: () => void;
  state: StreamState;
  count: number;
}

function StreamDot({ state, live }: { state: StreamState; live: boolean }) {
  const tone = !live
    ? "var(--swarm-muted)"
    : state === "open"
      ? "var(--swarm-success)"
      : state === "error"
        ? "var(--swarm-danger)"
        : "var(--swarm-warning)";
  const label = !live
    ? "Paused"
    : state === "open"
      ? "Live"
      : state === "error"
        ? "Reconnecting"
        : "Connecting";

  return (
    <span className="inline-flex items-center gap-1.5 text-xs font-medium text-[var(--swarm-muted)]">
      <span
        className={`h-1.5 w-1.5 rounded-full ${live && state !== "error" ? "animate-pulse motion-reduce:animate-none" : ""}`}
        style={{ background: tone }}
        aria-hidden
      />
      {label}
    </span>
  );
}

export function LogToolbar({
  nodes,
  selectedNodeId,
  onSelectNode,
  filter,
  onFilterChange,
  query,
  onQueryChange,
  live,
  onLiveChange,
  onClear,
  state,
  count,
}: LogToolbarProps) {
  const filters = Object.keys(LEVEL_FILTERS) as LevelFilter[];

  return (
    <div className="flex flex-wrap items-center gap-2 border-b border-[var(--swarm-border)] bg-[var(--swarm-chrome)] px-3 py-2">
      <select
        value={selectedNodeId ?? ""}
        onChange={(e) => onSelectNode(e.target.value || null)}
        aria-label="Select node"
        className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
      >
        <option value="" disabled>
          {nodes.length === 0 ? "No nodes" : "Select a node…"}
        </option>
        {nodes.map((n) => (
          <option key={n.id} value={n.id}>
            {n.name}
          </option>
        ))}
      </select>

      <div className="flex items-center rounded-md border border-[var(--swarm-border)] p-0.5" role="group" aria-label="Filter by level">
        {filters.map((f) => (
          <button
            key={f}
            type="button"
            onClick={() => onFilterChange(f)}
            aria-pressed={filter === f}
            className={`rounded px-2 py-1 text-xs font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)] ${
              filter === f
                ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
                : "text-[var(--swarm-muted)] hover:text-[var(--swarm-ink)]"
            }`}
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            {f}
          </button>
        ))}
      </div>

      <div className="relative min-w-[10rem] flex-1">
        <label htmlFor="log-search" className="sr-only">
          Search log messages
        </label>
        <IconSearch
          width={14}
          height={14}
          className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--swarm-muted)]"
        />
        <input
          id="log-search"
          type="search"
          value={query}
          onChange={(e) => onQueryChange(e.target.value)}
          placeholder="Filter messages…"
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] py-1.5 pl-8 pr-3 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
      </div>

      <span className="tabular-nums text-xs text-[var(--swarm-muted)]">{count} lines</span>

      <button
        type="button"
        onClick={() => onLiveChange(!live)}
        aria-pressed={live}
        disabled={!selectedNodeId}
        className="inline-flex items-center gap-1.5 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
        style={{ transitionDuration: "var(--swarm-duration)" }}
      >
        <StreamDot state={state} live={live} />
      </button>

      <button
        type="button"
        onClick={onClear}
        className="inline-flex items-center rounded-md px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        style={{ transitionDuration: "var(--swarm-duration)" }}
      >
        Clear
      </button>
    </div>
  );
}

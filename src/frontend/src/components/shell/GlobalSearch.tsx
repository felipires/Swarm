import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Node, Pipeline, TaskDefinition } from "../../store/store";
import {
  IconNodes,
  IconSearch,
  IconTasks,
  IconWorkflows,
} from "./icons";

type ResultKind = "node" | "pipeline" | "task";

interface Result {
  kind: ResultKind;
  id: string;
  label: string;
  sub?: string;
  href: string;
}

const KIND_LABEL: Record<ResultKind, string> = {
  node: "Node",
  pipeline: "Pipeline",
  task: "Task",
};

function KindIcon({ kind }: { kind: ResultKind }) {
  const cls = "shrink-0 text-[var(--swarm-muted)]";
  if (kind === "node") return <IconNodes width={13} height={13} className={cls} />;
  if (kind === "pipeline") return <IconWorkflows width={13} height={13} className={cls} />;
  return <IconTasks width={13} height={13} className={cls} />;
}

function match(q: string, ...fields: (string | undefined)[]): boolean {
  return fields.some((f) => f?.toLowerCase().includes(q));
}

const MAX_PER_KIND = 4;

export function GlobalSearch() {
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [cursor, setCursor] = useState(-1);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const { data: nodes } = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
    staleTime: 30_000,
  });
  const { data: pipelines } = useQuery({
    queryKey: queryKeys.pipelines,
    queryFn: () => apiClient.getPipelines(),
    staleTime: 30_000,
  });
  const { data: tasks } = useQuery({
    queryKey: queryKeys.tasks,
    queryFn: () => apiClient.getTasks(),
    staleTime: 30_000,
  });

  const results = useMemo<Result[]>(() => {
    const q = query.trim().toLowerCase();
    if (!q) return [];

    const nodeHits: Result[] = (nodes ?? [])
      .filter((n: Node) => match(q, n.name))
      .slice(0, MAX_PER_KIND)
      .map((n) => ({
        kind: "node",
        id: n.id,
        label: n.name,
        sub: n.status,
        href: `/overview`,
      }));

    const pipelineHits: Result[] = (pipelines ?? [])
      .filter((p: Pipeline) => !p.isDeleted && match(q, p.name, p.description))
      .slice(0, MAX_PER_KIND)
      .map((p) => ({
        kind: "pipeline",
        id: p.id,
        label: p.name,
        sub: p.description,
        href: `/workflows/${p.id}`,
      }));

    const taskHits: Result[] = (tasks ?? [])
      .filter((t: TaskDefinition) => !t.isDeleted && match(q, t.name, t.description))
      .slice(0, MAX_PER_KIND)
      .map((t) => ({
        kind: "task",
        id: t.id,
        label: t.name,
        sub: t.taskType,
        href: `/tasks`,
      }));

    return [...nodeHits, ...pipelineHits, ...taskHits];
  }, [query, nodes, pipelines, tasks]);

  // Reset cursor when results change
  useEffect(() => { setCursor(-1); }, [results]);

  function select(r: Result) {
    navigate(r.href);
    setQuery("");
    setOpen(false);
    inputRef.current?.blur();
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (!open || results.length === 0) return;
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setCursor((c) => Math.min(c + 1, results.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setCursor((c) => Math.max(c - 1, 0));
    } else if (e.key === "Enter" && cursor >= 0) {
      e.preventDefault();
      select(results[cursor]);
    } else if (e.key === "Escape") {
      setOpen(false);
      inputRef.current?.blur();
    }
  }

  // Scroll active item into view
  useEffect(() => {
    if (cursor < 0 || !listRef.current) return;
    const el = listRef.current.children[cursor] as HTMLElement | undefined;
    el?.scrollIntoView({ block: "nearest" });
  }, [cursor]);

  const showDropdown = open && results.length > 0;

  return (
    <div className="relative min-w-[12rem] flex-1 max-w-md">
      <label htmlFor="global-search" className="sr-only">
        Search nodes, pipelines, tasks
      </label>
      <IconSearch
        width={15}
        height={15}
        className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-[var(--swarm-muted)]"
      />
      <input
        ref={inputRef}
        id="global-search"
        type="search"
        value={query}
        onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onKeyDown={onKeyDown}
        placeholder="Search nodes, pipelines, tasks…"
        autoComplete="off"
        className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] py-1.5 pl-9 pr-3 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
      />

      {showDropdown && (
        <div className="absolute left-0 right-0 top-[calc(100%+4px)] z-[var(--z-dropdown,100)] overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] shadow-lg">
          <ul ref={listRef} role="listbox" className="max-h-72 overflow-y-auto py-1">
            {results.map((r, i) => (
              <li key={`${r.kind}-${r.id}`} role="option" aria-selected={cursor === i}>
                <button
                  type="button"
                  onMouseDown={() => select(r)}
                  onMouseEnter={() => setCursor(i)}
                  className={[
                    "flex w-full items-center gap-2.5 px-3 py-2 text-left text-sm transition-colors",
                    cursor === i
                      ? "bg-[var(--swarm-surface-raised)] text-[var(--swarm-ink)]"
                      : "text-[var(--swarm-ink)] hover:bg-[var(--swarm-surface-raised)]",
                  ].join(" ")}
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  <KindIcon kind={r.kind} />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate font-medium">{r.label}</span>
                    {r.sub && (
                      <span className="block truncate text-xs text-[var(--swarm-muted)]">
                        {r.sub}
                      </span>
                    )}
                  </span>
                  <span className="shrink-0 rounded bg-[var(--swarm-surface-raised)] px-1.5 py-0.5 text-xs text-[var(--swarm-muted)]">
                    {KIND_LABEL[r.kind]}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

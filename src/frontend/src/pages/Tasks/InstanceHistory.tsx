import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useId, useState } from "react";
import { IconChevron } from "../../components/shell/icons";
import { StatusPill } from "../../components/ui/StatusPill";
import { useTicker } from "../../hooks/useTicker";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { TaskInstance } from "../../store/store";
import { duration, relativeTime } from "../../utils/time";
import { InstanceDetail } from "./InstanceDetail";
import { INSTANCE_TONE, isInstanceActive } from "./instanceStatus";

interface InstanceHistoryProps {
  taskId: string;
}


interface InstanceRowProps {
  instance: TaskInstance;
  nodeName: string;
  now: number;
}

function InstanceRow({ instance, nodeName, now }: InstanceRowProps) {
  const [expanded, setExpanded] = useState(false);
  const panelId = useId();
  const start = instance.dispatchedAt ?? instance.createdAt;

  return (
    <li>
      <button
        type="button"
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
        aria-controls={panelId}
        className="flex w-full items-center gap-x-3 gap-y-1 rounded-md px-2 py-2 text-left text-sm transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        style={{ transitionDuration: "var(--swarm-duration)" }}
      >
        <span
          className="shrink-0 text-[var(--swarm-muted)] transition-transform"
          style={{
            transform: expanded ? "rotate(90deg)" : "none",
            transitionDuration: "var(--swarm-duration)",
            transitionTimingFunction: "var(--swarm-ease-out)",
          }}
          aria-hidden
        >
          <IconChevron direction="right" width={14} height={14} />
        </span>

        <span
          className="w-16 shrink-0 font-mono text-xs text-[var(--swarm-muted)]"
          title={instance.id}
        >
          {instance.id?.slice(0, 8) ?? "—"}
        </span>

        <span
          className="hidden w-28 shrink-0 truncate font-mono text-xs text-[var(--swarm-muted)] sm:inline"
          title={instance.nodeId ?? undefined}
        >
          {nodeName}
        </span>

        <span
          className="w-20 shrink-0 tabular-nums text-[var(--swarm-muted)]"
          title={instance.createdAt}
        >
          {relativeTime(instance.createdAt, now)}
        </span>

        <span className="hidden w-16 shrink-0 tabular-nums text-[var(--swarm-muted)] sm:inline">
          {duration(start, instance.completedAt, now)}
        </span>

        <StatusPill
          tone={INSTANCE_TONE[instance.status]}
          label={instance.status}
          pulsing={isInstanceActive(instance.status)}
        />

        <span className="min-w-0 flex-1 truncate text-right font-mono text-xs text-[var(--swarm-danger)]">
          {!expanded && instance.errorMessage ? instance.errorMessage : ""}
        </span>
      </button>

      {expanded && (
        <div id={panelId}>
          <InstanceDetail instance={instance} nodeName={nodeName} now={now} />
        </div>
      )}
    </li>
  );
}

export function InstanceHistory({ taskId }: InstanceHistoryProps) {
  const now = useTicker(5_000);
  const query = useInfiniteQuery({
    queryKey: queryKeys.taskInstances(taskId),
    queryFn: ({ pageParam }) => apiClient.getInstances(taskId, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : undefined),
    refetchInterval: 15_000,
  });

  const nodesQuery = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
    staleTime: 30_000,
  });
  const nodeNames = new Map((nodesQuery.data ?? []).map((n) => [n.id, n.name]));
  const nameFor = (id: string | null | undefined) => {
    if (!id) return "unclaimed";
    return nodeNames.get(id) ?? `${id.slice(0, 8)}…`;
  };

  if (query.isLoading) {
    return (
      <div
        className="space-y-1.5"
        aria-busy="true"
        aria-label="Loading instances"
      >
        {Array.from({ length: 3 }).map((_, i) => (
          <div
            key={i}
            className="h-9 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none"
          />
        ))}
      </div>
    );
  }

  if (query.isError) {
    return (
      <p className="text-sm text-[var(--swarm-danger)]" role="alert">
        Could not load instances.
      </p>
    );
  }

  const instances = query.data?.pages.flatMap((p) => p.items) ?? [];

  if (instances.length === 0) {
    return (
      <p className="text-sm text-[var(--swarm-muted)]">No dispatches yet.</p>
    );
  }

  return (
    <div>
      <ul className="divide-y divide-[var(--swarm-border)]">
        {instances.map((inst, i) => (
          <InstanceRow
            key={inst.id ?? `instance-${i}`}
            instance={inst}
            nodeName={nameFor(inst.nodeId)}
            now={now}
          />
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
          {query.isFetchingNextPage ? "Loading…" : "Show older dispatches"}
        </button>
      )}
    </div>
  );
}

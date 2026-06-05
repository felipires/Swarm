import { useQuery } from "@tanstack/react-query";
import { StatusPill } from "../../components/ui/StatusPill";
import { useTicker } from "../../hooks/useTicker";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { absoluteTime, duration, relativeTime } from "../../utils/time";
import { INSTANCE_TONE, isInstanceActive } from "./instanceStatus";

interface InstanceHistoryProps {
  taskId: string;
}

const MAX_VISIBLE = 10;

export function InstanceHistory({ taskId }: InstanceHistoryProps) {
  const now = useTicker(5_000);
  const query = useQuery({
    queryKey: queryKeys.taskInstances(taskId),
    queryFn: () => apiClient.getInstances(taskId),
    refetchInterval: 15_000,
  });

  if (query.isLoading) {
    return (
      <div className="space-y-1.5" aria-busy="true" aria-label="Loading instances">
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
        Could not load instances.
      </p>
    );
  }

  const instances = [...(query.data ?? [])].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );

  if (instances.length === 0) {
    return <p className="text-sm text-[var(--swarm-muted)]">No dispatches yet.</p>;
  }

  const visible = instances.slice(0, MAX_VISIBLE);
  const hidden = instances.length - visible.length;

  return (
    <div>
      <ul className="divide-y divide-[var(--swarm-border)]">
        {visible.map((inst) => {
          const start = inst.dispatchedAt ?? inst.createdAt;
          return (
            <li
              key={inst.id}
              className="flex flex-wrap items-center gap-x-4 gap-y-1 py-2 text-sm"
            >
              <span className="font-mono text-xs text-[var(--swarm-muted)]" title={inst.id}>
                {inst.id.slice(0, 8)}
              </span>
              <span
                className="tabular-nums text-[var(--swarm-muted)]"
                title={absoluteTime(inst.createdAt)}
              >
                {relativeTime(inst.createdAt, now)}
              </span>
              <span className="tabular-nums text-[var(--swarm-muted)]">
                {duration(start, inst.completedAt, now)}
              </span>
              <StatusPill
                tone={INSTANCE_TONE[inst.status]}
                label={inst.status}
                pulsing={isInstanceActive(inst.status)}
              />
              {inst.errorMessage && (
                <span
                  className="min-w-0 flex-1 truncate font-mono text-xs text-[var(--swarm-danger)]"
                  title={inst.errorMessage}
                >
                  {inst.errorMessage}
                </span>
              )}
            </li>
          );
        })}
      </ul>
      {hidden > 0 && (
        <p className="mt-2 text-xs text-[var(--swarm-muted)]">
          Showing the {MAX_VISIBLE} most recent of {instances.length} dispatches.
        </p>
      )}
    </div>
  );
}

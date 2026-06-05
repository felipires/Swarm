import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { IconChevron, IconTrash } from "../../components/shell/icons";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { TaskDefinition } from "../../store/store";
import { relativeTime } from "../../utils/time";
import { DispatchControl } from "./DispatchControl";
import { InstanceHistory } from "./InstanceHistory";

interface TaskRowProps {
  task: TaskDefinition;
  now: number;
}

export function TaskRow({ task, now }: TaskRowProps) {
  const [expanded, setExpanded] = useState(false);
  const panelId = useId();
  const queryClient = useQueryClient();

  const remove = useMutation({
    mutationFn: () => apiClient.deleteTask(task.id),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: queryKeys.tasks });
      const previous = queryClient.getQueryData<TaskDefinition[]>(queryKeys.tasks);
      queryClient.setQueryData<TaskDefinition[]>(queryKeys.tasks, (old) =>
        (old ?? []).filter((t) => t.id !== task.id),
      );
      return { previous };
    },
    onError: (_e, _v, ctx) => {
      if (ctx?.previous) queryClient.setQueryData(queryKeys.tasks, ctx.previous);
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: queryKeys.tasks }),
  });

  const handleDelete = () => {
    if (window.confirm(`Delete task "${task.name}"? This cannot be undone.`)) {
      remove.mutate();
    }
  };

  return (
    <div className="border-b border-[var(--swarm-border)] last:border-b-0">
      <div className="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          onClick={() => setExpanded((e) => !e)}
          aria-expanded={expanded}
          aria-controls={panelId}
          className="flex min-w-0 flex-1 items-center gap-3 rounded-md text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
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
            <IconChevron direction="right" width={16} height={16} />
          </span>
          <span className="min-w-0">
            <span className="block truncate font-medium text-[var(--swarm-ink)]">
              {task.name}
            </span>
            {task.description && (
              <span className="block truncate text-xs text-[var(--swarm-muted)]">
                {task.description}
              </span>
            )}
          </span>
        </button>

        <span
          className="shrink-0 tabular-nums text-sm text-[var(--swarm-muted)]"
          title={`Updated ${task.updatedAt}`}
        >
          {relativeTime(task.updatedAt, now)}
        </span>

        <button
          type="button"
          onClick={handleDelete}
          disabled={remove.isPending}
          aria-label={`Delete task ${task.name}`}
          className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-danger-subtle)] hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-40"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <IconTrash width={15} height={15} />
        </button>
      </div>

      {expanded && (
        <div id={panelId} className="space-y-5 px-4 pb-4 pl-11">
          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Dispatch
            </h3>
            <DispatchControl taskId={task.id} />
          </section>

          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Config
            </h3>
            <pre className="max-h-48 overflow-auto rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] p-3 font-mono text-xs text-[var(--swarm-ink)]">
              {task.configJson}
            </pre>
          </section>

          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Recent instances
            </h3>
            <InstanceHistory taskId={task.id} />
          </section>
        </div>
      )}
    </div>
  );
}

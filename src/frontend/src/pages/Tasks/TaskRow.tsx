import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { IconChevron } from "../../components/shell/icons";
import { ConfirmDeleteButton } from "../../components/ui/ConfirmDeleteButton";
import { JsonViewer } from "../../components/ui/JsonViewer";
import { VersionBadge } from "../../components/ui/VersionBadge";
import { VersionHistory } from "../../components/ui/VersionHistory";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { TaskDefinition } from "../../store/store";
import { relativeTime } from "../../utils/time";
import { CreateTaskForm } from "./CreateTaskForm";
import { DispatchControl } from "./DispatchControl";
import { InstanceHistory } from "./InstanceHistory";

function CollapsibleSection({
  title,
  children,
  defaultOpen = false,
}: {
  title: string;
  children: React.ReactNode;
  defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  const id = useId();
  return (
    <section>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={id}
        className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)] transition-colors hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        style={{ transitionDuration: "var(--swarm-duration)" }}
      >
        <IconChevron
          direction="right"
          width={12}
          height={12}
          style={{
            transform: open ? "rotate(90deg)" : "none",
            transitionDuration: "var(--swarm-duration)",
            transitionTimingFunction: "var(--swarm-ease-out)",
          }}
          aria-hidden
        />
        {title}
      </button>
      {open && <div id={id} className="mt-2">{children}</div>}
    </section>
  );
}

/** Renders a task version snapshot (create-request shape) as config JSON. */
function TaskSnapshotView({ snapshot }: { snapshot: unknown }) {
  const s = snapshot as {
    taskType?: string;
    configJson?: string;
    description?: string;
  };
  return (
    <div className="space-y-2">
      {s.taskType && (
        <p className="font-mono text-xs text-[var(--swarm-muted)]">
          {s.taskType}
        </p>
      )}
      <JsonViewer value={s.configJson ?? "{}"} label="Config" />
    </div>
  );
}

interface TaskRowProps {
  task: TaskDefinition;
  now: number;
}

export function TaskRow({ task, now }: TaskRowProps) {
  const [expanded, setExpanded] = useState(false);
  const [editing, setEditing] = useState(false);
  const panelId = useId();
  const queryClient = useQueryClient();

  const remove = useMutation({
    mutationFn: () => apiClient.deleteTask(task.id),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: queryKeys.tasks });
      const previous = queryClient.getQueryData<TaskDefinition[]>(
        queryKeys.tasks,
      );
      queryClient.setQueryData<TaskDefinition[]>(queryKeys.tasks, (old) =>
        (old ?? []).filter((t) => t.id !== task.id),
      );
      return { previous };
    },
    onError: (_e, _v, ctx) => {
      if (ctx?.previous)
        queryClient.setQueryData(queryKeys.tasks, ctx.previous);
    },
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.tasks }),
  });

  const undelete = useMutation({
    mutationFn: () => apiClient.undeleteTask(task.id),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.tasks }),
  });

  if (task.isDeleted) {
    return (
      <div className="flex items-center gap-3 border-b border-[var(--swarm-border)] px-4 py-3 last:border-b-0">
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <span className="truncate font-medium text-[var(--swarm-muted)] line-through">
              {task.name}
            </span>
            <span className="rounded-full bg-[var(--swarm-danger-subtle)] px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-danger)]">
              deleted
            </span>
          </span>
        </span>
        <button
          type="button"
          onClick={() => undelete.mutate()}
          disabled={undelete.isPending}
          className="inline-flex shrink-0 items-center rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-xs font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {undelete.isPending ? "Restoring…" : "Restore"}
        </button>
      </div>
    );
  }

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

        {task.version != null && <VersionBadge version={task.version} />}

        <span
          className="shrink-0 tabular-nums text-sm text-[var(--swarm-muted)]"
          title={`Updated ${task.updatedAt}`}
        >
          {relativeTime(task.updatedAt, now)}
        </span>

        <ConfirmDeleteButton
          onConfirm={() => remove.mutate()}
          disabled={remove.isPending}
          label={`Delete task ${task.name}`}
        />
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
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
                Config
              </h3>
              <button
                type="button"
                onClick={() => setEditing((e) => !e)}
                aria-expanded={editing}
                className="rounded px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                style={{ transitionDuration: "var(--swarm-duration)" }}
              >
                {editing ? "Cancel edit" : "Edit"}
              </button>
            </div>
            {editing ? (
              <CreateTaskForm task={task} onClose={() => setEditing(false)} />
            ) : (
              <pre className="max-h-48 overflow-auto rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] p-3 font-mono text-xs text-[var(--swarm-ink)]">
                {task.configJson}
              </pre>
            )}
          </section>

          <CollapsibleSection title="Version history">
            <VersionHistory
              kind="task"
              entityId={task.id}
              currentVersion={task.version}
              versionsKey={queryKeys.taskVersions(task.id)}
              onRestored={() =>
                queryClient.invalidateQueries({ queryKey: queryKeys.tasks })
              }
              renderSnapshot={(snap) => <TaskSnapshotView snapshot={snap} />}
              now={now}
            />
          </CollapsibleSection>

          <CollapsibleSection title="Recent instances">
            <InstanceHistory taskId={task.id} />
          </CollapsibleSection>
        </div>
      )}
    </div>
  );
}

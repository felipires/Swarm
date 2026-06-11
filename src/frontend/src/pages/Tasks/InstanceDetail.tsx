import { JsonViewer } from "../../components/ui/JsonViewer";
import type { TaskInstance } from "../../store/store";
import { absoluteTime, duration, relativeTime } from "../../utils/time";

interface InstanceDetailProps {
  instance: TaskInstance;
  nodeName: string;
  now: number;
}

interface Stage {
  label: string;
  at?: string;
}

function Timeline({ instance, now }: { instance: TaskInstance; now: number }) {
  const stages: Stage[] = [
    { label: "Created", at: instance.createdAt },
    { label: "Dispatched", at: instance.dispatchedAt },
    {
      label: instance.status === "Failed" ? "Failed" : "Completed",
      at: instance.completedAt,
    },
  ];

  return (
    <ol className="flex flex-col gap-2">
      {stages.map((stage, i) => {
        const reached = Boolean(stage.at);
        const isLast = i === stages.length - 1;
        return (
          <li key={stage.label} className="flex items-start gap-3">
            <span className="relative mt-1 flex flex-col items-center">
              <span
                className="h-2 w-2 rounded-full"
                style={{
                  background: reached
                    ? "var(--swarm-primary)"
                    : "var(--swarm-border-strong)",
                }}
                aria-hidden
              />
              {!isLast && (
                <span
                  className="absolute top-2 h-[calc(100%+0.25rem)] w-px"
                  style={{ background: "var(--swarm-border)" }}
                  aria-hidden
                />
              )}
            </span>
            <span className="flex min-w-0 flex-col">
              <span
                className={`text-sm ${reached ? "text-[var(--swarm-ink)]" : "text-[var(--swarm-muted)]"}`}
              >
                {stage.label}
              </span>
              {reached ? (
                <span
                  className="text-xs text-[var(--swarm-muted)]"
                  title={absoluteTime(stage.at!)}
                >
                  {absoluteTime(stage.at!)} · {relativeTime(stage.at!, now)}
                </span>
              ) : (
                <span className="text-xs text-[var(--swarm-placeholder)]">
                  pending
                </span>
              )}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

function Field({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
        {label}
      </span>
      <span className="text-sm text-[var(--swarm-ink)]">{children}</span>
    </div>
  );
}

export function InstanceDetail({
  instance,
  nodeName,
  now,
}: InstanceDetailProps) {
  const start = instance.dispatchedAt ?? instance.createdAt;

  return (
    <div className="space-y-4 border-t border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-3">
      <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_auto]">
        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Instance ID">
            <span className="font-mono text-xs" title={instance.id}>
              {instance.id}
            </span>
          </Field>
          <Field label="Node">
            <span
              className="font-mono text-xs"
              title={instance.nodeId ?? undefined}
            >
              {nodeName}
            </span>
          </Field>
          <Field label="Duration">
            <span className="tabular-nums">
              {duration(start, instance.completedAt, now)}
            </span>
          </Field>
        </div>

        <div className="sm:border-l sm:border-[var(--swarm-border)] sm:pl-4">
          <Timeline instance={instance} now={now} />
        </div>
      </div>

      {instance.runtimeParamsJson && (
        <JsonViewer value={instance.runtimeParamsJson} label="Runtime Params" />
      )}

      {instance.errorMessage && (
        <div className="rounded-md border border-[var(--swarm-danger)]/30 bg-[var(--swarm-danger-subtle)] px-3 py-2">
          <p className="text-xs font-medium uppercase tracking-wide text-[var(--swarm-danger)]">
            Error
          </p>
          <p className="mt-1 whitespace-pre-wrap font-mono text-xs text-[var(--swarm-danger)]">
            {instance.errorMessage}
          </p>
        </div>
      )}

      {instance.resultJson ? (
        <JsonViewer value={instance.resultJson} label="Result" />
      ) : (
        !instance.errorMessage && (
          <p className="text-sm text-[var(--swarm-muted)]">
            No result recorded{instance.status === "Running" ? " yet" : ""}.
          </p>
        )
      )}
    </div>
  );
}

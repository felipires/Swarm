import { useQuery } from "@tanstack/react-query";
import { JsonViewer } from "../../../components/ui/JsonViewer";
import { StatusPill } from "../../../components/ui/StatusPill";
import { apiClient } from "../../../services/api";
import { queryKeys } from "../../../services/queryKeys";
import type { PipelineStep, PipelineStepInstance } from "../../../store/store";
import { absoluteTime, duration } from "../../../utils/time";
import { LogResults } from "../../Observability/LogResults";
import { isStepActive, STEP_STATUS_TONE } from "./stepStatus";

interface StepDetailProps {
  step: PipelineStep;
  taskName: string;
  /** The step's instance in the selected run, if a run is selected. */
  instance: PipelineStepInstance | null;
  /** All steps in the pipeline — to find which consume this step's output. */
  allSteps: PipelineStep[];
  onClose: () => void;
}

export function StepDetail({ step, taskName, instance, allSteps, onClose }: StepDetailProps) {
  const mappings = step.outputMappings ?? [];
  const downstreamCount = allSteps.filter((s) =>
    s.outputMappings?.some((m) => m.fromStep === step.name),
  ).length;

  const taskInstanceId = instance?.taskInstanceId ?? null;
  const taskInstanceQuery = useQuery({
    queryKey: queryKeys.taskInstance(taskInstanceId ?? ""),
    queryFn: () => apiClient.getInstance(taskInstanceId!),
    enabled: Boolean(taskInstanceId),
  });
  const taskInstance = taskInstanceQuery.data;

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-start justify-between gap-2 border-b border-[var(--swarm-border)] px-4 py-2.5">
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold text-[var(--swarm-ink)]" title={step.name}>
            {step.name}
          </p>
          <p className="truncate font-mono text-xs text-[var(--swarm-muted)]">{taskName}</p>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close step detail"
          className="rounded-md px-1.5 py-0.5 text-sm text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          ×
        </button>
      </div>

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-3">
        {!instance ? (
          <p className="text-sm text-[var(--swarm-muted)]">
            Select a run to see this step's status and result.
          </p>
        ) : (
          <>
            <div className="flex items-center gap-2">
              <StatusPill
                tone={STEP_STATUS_TONE[instance.status]}
                label={instance.status}
                pulsing={isStepActive(instance.status)}
              />
              {instance.dispatchedAt && (
                <span className="text-xs text-[var(--swarm-muted)]">
                  {duration(instance.dispatchedAt, instance.completedAt)}
                </span>
              )}
            </div>

            <dl className="space-y-1 text-xs">
              {instance.dispatchedAt && (
                <div className="flex justify-between gap-3">
                  <dt className="text-[var(--swarm-muted)]">Dispatched</dt>
                  <dd className="text-[var(--swarm-ink)]">{absoluteTime(instance.dispatchedAt)}</dd>
                </div>
              )}
              {instance.completedAt && (
                <div className="flex justify-between gap-3">
                  <dt className="text-[var(--swarm-muted)]">Completed</dt>
                  <dd className="text-[var(--swarm-ink)]">{absoluteTime(instance.completedAt)}</dd>
                </div>
              )}
            </dl>

            {step.runtimeParamsJson && (
              <JsonViewer value={step.runtimeParamsJson} label="Step params (static)" />
            )}

            {mappings.length > 0 && (
              <div>
                <p className="text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
                  Mapped inputs
                </p>
                <dl className="mt-1 space-y-0.5 text-xs">
                  {mappings.map((m, i) => (
                    <div key={i} className="flex flex-wrap items-center gap-1">
                      <span className="font-mono text-[var(--swarm-ink)]">{m.fromPath}</span>
                      <span className="text-[var(--swarm-muted)]" aria-hidden>→</span>
                      <span className="font-mono text-[var(--swarm-ink)]">{m.toParam}</span>
                      <span className="text-[var(--swarm-muted)]">from {m.fromStep}</span>
                    </div>
                  ))}
                </dl>
              </div>
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

            {taskInstanceId ? (
              taskInstanceQuery.isLoading ? (
                <div className="h-16 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
              ) : (
                <>
                  {taskInstance?.runtimeParamsJson && (
                    <JsonViewer value={taskInstance.runtimeParamsJson} label="Params (input)" />
                  )}
                  {taskInstance?.configJsonSnapshot && (
                    <JsonViewer value={taskInstance.configJsonSnapshot} label="Config (snapshot)" />
                  )}
                  {taskInstance?.resultJson && (
                    <JsonViewer
                      value={taskInstance.resultJson}
                      label={
                        downstreamCount > 0
                          ? `Result (output) · used by ${downstreamCount} downstream step${downstreamCount === 1 ? "" : "s"}`
                          : "Result (output)"
                      }
                    />
                  )}
                  {!taskInstance?.resultJson && !instance.errorMessage && (
                    <p className="text-sm text-[var(--swarm-muted)]">
                      No result recorded{instance.status === "Dispatched" ? " yet" : ""}.
                    </p>
                  )}
                </>
              )
            ) : (
              instance.status !== "Failed" && (
                <p className="text-sm text-[var(--swarm-muted)]">
                  {instance.status === "Skipped"
                    ? "Skipped: an upstream step did not complete."
                    : "Not dispatched yet."}
                </p>
              )
            )}

            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
                Logs
              </p>
              <div className="h-48 overflow-hidden rounded-md border border-[var(--swarm-border)]">
                <LogResults
                  params={{ tags: [`step:${step.id}`] }}
                  refetchMs={isStepActive(instance.status) ? 5_000 : 0}
                  emptyHint="No logs for this step yet."
                />
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

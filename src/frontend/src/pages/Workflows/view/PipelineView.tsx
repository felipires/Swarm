import {
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { IconChevron } from "../../../components/shell/icons";
import { StatusPill } from "../../../components/ui/StatusPill";
import { VersionBadge } from "../../../components/ui/VersionBadge";
import { VersionHistory } from "../../../components/ui/VersionHistory";
import { useTicker } from "../../../hooks/useTicker";
import { apiClient } from "../../../services/api";
import { queryKeys } from "../../../services/queryKeys";
import type { PipelineRun, PipelineStepInstance } from "../../../store/store";
import { absoluteTime, duration, relativeTime } from "../../../utils/time";
import { FAILURE_LABEL, STRATEGY_LABEL } from "../pipelineGraph";
import { isRunning, RUN_TONE } from "../runStatus";
import { ScheduleChip } from "../ScheduleChip";
import { parseParamsWithPlaceholders } from "../../../utils/placeholderJson";
import { PipelineCanvas } from "./PipelineCanvas";
import { LogResults } from "../../Observability/LogResults";
import { StepDetail } from "./StepDetail";

/** Renders a pipeline version snapshot (draft shape) as a compact step list. */
function PipelineSnapshotView({ snapshot }: { snapshot: unknown }) {
  const s = snapshot as {
    steps?: Array<{
      name: string;
      dependsOn?: string[];
      strategy?: keyof typeof STRATEGY_LABEL | null;
      failurePolicy?: keyof typeof FAILURE_LABEL;
    }>;
  };
  const steps = s.steps ?? [];
  if (steps.length === 0) return <p className="text-xs text-[var(--swarm-muted)]">No steps.</p>;
  return (
    <ul className="space-y-1.5">
      {steps.map((st) => (
        <li key={st.name} className="text-xs">
          <span className="font-medium text-[var(--swarm-ink)]">{st.name}</span>
          {st.dependsOn && st.dependsOn.length > 0 && (
            <span className="text-[var(--swarm-muted)]"> ← {st.dependsOn.join(", ")}</span>
          )}
          <span className="block text-[var(--swarm-muted)]">
            {st.strategy ? STRATEGY_LABEL[st.strategy] : "inherited"} ·{" "}
            {st.failurePolicy ? FAILURE_LABEL[st.failurePolicy] : "fail"}
          </span>
        </li>
      ))}
    </ul>
  );
}

const parseParams = parseParamsWithPlaceholders;

export function PipelineView() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const now = useTicker(5_000);

  const [drawerOpen, setDrawerOpen] = useState(true);
  const [drawerTab, setDrawerTab] = useState<"runs" | "versions" | "logs">("runs");
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [selectedStepId, setSelectedStepId] = useState<string | null>(null);
  const [runOpen, setRunOpen] = useState(false);
  const [paramsRaw, setParamsRaw] = useState("");

  const pipelineQuery = useQuery({
    queryKey: queryKeys.pipeline(id),
    queryFn: () => apiClient.getPipeline(id),
    enabled: id !== "",
  });
  const tasksQuery = useQuery({
    queryKey: queryKeys.tasks,
    queryFn: () => apiClient.getTasks(),
  });
  const pipeline = pipelineQuery.data;

  const runsQuery = useInfiniteQuery({
    queryKey: queryKeys.pipelineRuns(id),
    queryFn: ({ pageParam }) => apiClient.getPipelineRuns(id, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => (last.hasMore ? last.nextCursor : undefined),
    enabled: id !== "",
  });
  const runs: PipelineRun[] = useMemo(
    () => runsQuery.data?.pages.flatMap((p) => p.items) ?? [],
    [runsQuery.data],
  );

  // Step instances for the selected run, refreshed while it's still running.
  const stepsQuery = useQuery({
    queryKey: queryKeys.pipelineRunSteps(selectedRunId ?? ""),
    queryFn: () => apiClient.getRunSteps(selectedRunId!),
    enabled: Boolean(selectedRunId),
    refetchInterval: (q) => {
      const data = q.state.data as PipelineStepInstance[] | undefined;
      const active = data?.some(
        (s) => s.status === "Dispatched" || s.status === "Waiting",
      );
      return active ? 4000 : false;
    },
  });

  const stepStatusById = useMemo(() => {
    const map = new Map<string, PipelineStepInstance>();
    for (const s of stepsQuery.data ?? []) map.set(s.pipelineStepId, s);
    return map;
  }, [stepsQuery.data]);

  const run = useMutation({
    mutationFn: () => {
      const parsed = parseParams(paramsRaw);
      return apiClient.runPipeline(id, parsed.ok ? parsed.value : undefined);
    },
    onSuccess: (created) => {
      setRunOpen(false);
      setParamsRaw("");
      setSelectedRunId(created.id);
      queryClient.invalidateQueries({ queryKey: queryKeys.pipelineRuns(id) });
    },
  });
  const paramsValid = parseParams(paramsRaw).ok;

  const selectedRun = runs.find((r) => r.id === selectedRunId) ?? null;
  const retry = useMutation({
    mutationFn: (runId: string) => apiClient.retryFailedRun(runId),
    onSuccess: (created) => {
      setSelectedRunId(created.id);
      queryClient.invalidateQueries({ queryKey: queryKeys.pipelineRuns(id) });
    },
  });

  const selectedStep =
    pipeline?.steps.find((s) => s.id === selectedStepId) ?? null;
  const taskNameFor = (taskId: string) =>
    tasksQuery.data?.find((t) => t.id === taskId)?.name ??
    `${taskId.slice(0, 8)}…`;

  return (
    <div className="flex h-full flex-col">
      <header className="flex flex-wrap items-center gap-3 border-b border-[var(--swarm-border)] bg-[var(--swarm-chrome)] px-4 py-2.5">
        <button
          type="button"
          onClick={() => navigate("/workflows")}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <IconChevron direction="left" width={16} height={16} />
          Workflows
        </button>

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h1 className="truncate text-base font-semibold text-[var(--swarm-ink)]">
              {pipeline?.name ?? "Pipeline"}
            </h1>
            {pipeline?.version != null && <VersionBadge version={pipeline.version} />}
            {pipeline && <ScheduleChip pipelineId={pipeline.id} />}
          </div>
          {pipeline?.description && (
            <p className="truncate text-xs text-[var(--swarm-muted)]">
              {pipeline.description}
            </p>
          )}
        </div>

        <button
          type="button"
          onClick={() => navigate(`/workflows/${id}/edit`)}
          disabled={!pipeline}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Edit
        </button>
        <button
          type="button"
          onClick={() => setRunOpen((o) => !o)}
          disabled={!pipeline}
          className="rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Run
        </button>
        <button
          type="button"
          onClick={() => setDrawerOpen((o) => !o)}
          aria-pressed={drawerOpen}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {drawerOpen ? "Hide runs" : "Runs"}
        </button>
      </header>

      {runOpen && (
        <div className="flex flex-wrap items-start gap-3 border-b border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-4 py-3">
          <div className="min-w-[16rem] flex-1">
            <label
              htmlFor="canvas-run-params"
              className="mb-1 block text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]"
            >
              Runtime params{" "}
              <span className="font-normal normal-case">(JSON, optional)</span>
            </label>
            <textarea
              id="canvas-run-params"
              value={paramsRaw}
              onChange={(e) => setParamsRaw(e.target.value)}
              rows={2}
              spellCheck={false}
              placeholder={'{ "since": "2026-01-01" }'}
              aria-invalid={!paramsValid || undefined}
              className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
            />
            {!paramsValid && (
              <p className="mt-1 text-xs text-[var(--swarm-danger)]">
                Params must be a JSON object.
              </p>
            )}
            {run.isError && (
              <p
                className="mt-1 text-xs text-[var(--swarm-danger)]"
                role="alert"
              >
                Could not start the run. Check that matching nodes are online.
              </p>
            )}
          </div>
          <button
            type="button"
            onClick={() => run.mutate()}
            disabled={!paramsValid || run.isPending}
            className="mt-5 rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            {run.isPending ? "Starting…" : "Start run"}
          </button>
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        <div className="relative min-w-0 flex-1">
          {pipelineQuery.isLoading ? (
            <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
              Loading pipeline…
            </div>
          ) : pipelineQuery.isError || !pipeline ? (
            <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-danger)]">
              Could not load this pipeline.
            </div>
          ) : (
            <PipelineCanvas
              pipeline={pipeline}
              tasks={tasksQuery.data ?? []}
              stepStatusById={stepStatusById}
              selectedStepId={selectedStepId}
              onSelectStep={setSelectedStepId}
            />
          )}

          {/* Step detail floats over the canvas when a step is selected. */}
          {pipeline && selectedStep && (
            <div className="absolute bottom-4 right-4 top-4 z-10 w-2/6 overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] shadow-lg">
              <StepDetail
                step={selectedStep}
                taskName={taskNameFor(selectedStep.taskDefinitionId)}
                instance={stepStatusById.get(selectedStep.id) ?? null}
                allSteps={pipeline.steps}
                onClose={() => setSelectedStepId(null)}
              />
            </div>
          )}
        </div>

        {drawerOpen && pipeline && (
          <aside
            className="flex w-80 shrink-0 flex-col overflow-hidden border-l border-[var(--swarm-border)] bg-[var(--swarm-surface)]"
            aria-label="Run history and versions"
          >
            <div className="flex items-center gap-1 border-b border-[var(--swarm-border)] px-2 py-1.5">
              {(["runs", "logs", "versions"] as const).map((tab) => (
                <button
                  key={tab}
                  type="button"
                  onClick={() => setDrawerTab(tab)}
                  aria-pressed={drawerTab === tab}
                  className={`rounded-md px-2.5 py-1 text-xs font-medium capitalize transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)] ${
                    drawerTab === tab
                      ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
                      : "text-[var(--swarm-muted)] hover:text-[var(--swarm-ink)]"
                  }`}
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  {tab}
                </button>
              ))}
              {drawerTab === "runs" && selectedRunId && (
                <button
                  type="button"
                  onClick={() => setSelectedRunId(null)}
                  className="ml-auto text-xs font-medium text-[var(--swarm-primary)] hover:underline"
                >
                  Clear
                </button>
              )}
            </div>

            {drawerTab === "versions" ? (
              <div className="min-h-0 flex-1 overflow-y-auto p-3">
                <VersionHistory
                  kind="pipeline"
                  entityId={pipeline.id}
                  currentVersion={pipeline.version}
                  versionsKey={queryKeys.pipelineVersions(pipeline.id)}
                  onRestored={() => queryClient.invalidateQueries({ queryKey: queryKeys.pipeline(id) })}
                  renderSnapshot={(snap) => <PipelineSnapshotView snapshot={snap} />}
                  now={now}
                />
              </div>
            ) : drawerTab === "logs" ? (
              <div className="min-h-0 flex-1 overflow-hidden">
                {selectedRunId ? (
                  <LogResults
                    params={{ tags: [`run:${selectedRunId}`] }}
                    refetchMs={selectedRun && isRunning(selectedRun.status) ? 5_000 : 0}
                    emptyHint="No logs for this run yet."
                  />
                ) : (
                  <p className="p-3 text-sm text-[var(--swarm-muted)]">
                    Select a run to see its logs.
                  </p>
                )}
              </div>
            ) : (
            <div className="min-h-0 flex-1 overflow-y-auto p-2">
              {selectedRun?.status === "Failed" && (
                <button
                  type="button"
                  onClick={() => retry.mutate(selectedRun.id)}
                  disabled={retry.isPending}
                  className="mb-2 w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2 py-1.5 text-xs font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  {retry.isPending ? "Retrying…" : "Retry failed steps"}
                </button>
              )}
              {retry.isError && (
                <p role="alert" className="mb-2 text-xs text-[var(--swarm-danger)]">
                  Could not retry this run.
                </p>
              )}
              {runsQuery.isLoading ? (
                <div className="space-y-1.5 p-2">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <div
                      key={i}
                      className="h-12 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none"
                    />
                  ))}
                </div>
              ) : runs.length === 0 ? (
                <p className="p-2 text-sm text-[var(--swarm-muted)]">
                  No runs yet.
                </p>
              ) : (
                <ul className="space-y-1">
                  {runs.map((r) => {
                    const isSelected = r.id === selectedRunId;
                    return (
                      <li key={r.id}>
                        <button
                          type="button"
                          onClick={() => {
                            setSelectedRunId(r.id);
                            setSelectedStepId(null);
                          }}
                          aria-current={isSelected ? "true" : undefined}
                          className={`w-full rounded-md px-2.5 py-2 text-left transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] ${
                            isSelected
                              ? "bg-[var(--swarm-primary-subtle)]"
                              : "hover:bg-[var(--swarm-surface-raised)]"
                          }`}
                          style={{
                            transitionDuration: "var(--swarm-duration)",
                          }}
                        >
                          <div className="flex items-center justify-between gap-2">
                            <StatusPill
                              tone={RUN_TONE[r.status]}
                              label={r.status}
                              pulsing={isRunning(r.status)}
                            />
                            <span
                              className="text-xs tabular-nums text-[var(--swarm-muted)]"
                              title={absoluteTime(r.startedAt)}
                            >
                              {relativeTime(r.startedAt, now)}
                            </span>
                          </div>
                          <div className="mt-1 flex items-center justify-between gap-2 text-xs text-[var(--swarm-muted)]">
                            <span className="tabular-nums">
                              {duration(r.startedAt, r.completedAt, now)}
                            </span>
                            {r.errorMessage && (
                              <span
                                className="truncate font-mono text-[var(--swarm-danger)]"
                                title={r.errorMessage}
                              >
                                {r.errorMessage}
                              </span>
                            )}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )}

              {runsQuery.hasNextPage && (
                <button
                  type="button"
                  onClick={() => runsQuery.fetchNextPage()}
                  disabled={runsQuery.isFetchingNextPage}
                  className="mt-2 w-full rounded-md px-2 py-1.5 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  {runsQuery.isFetchingNextPage
                    ? "Loading…"
                    : "Show older runs"}
                </button>
              )}
            </div>
            )}
          </aside>
        )}
      </div>
    </div>
  );
}

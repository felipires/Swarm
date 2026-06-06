import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { useNavigate } from "react-router-dom";
import { IconChevron } from "../../components/shell/icons";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Pipeline } from "../../store/store";
import { RunHistory } from "./RunHistory";
import { ScheduleChip } from "./ScheduleChip";
import { SchedulesPanel } from "./SchedulesPanel";
import { StepGraph } from "./StepGraph";

function IconPlay() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 14 14"
      fill="currentColor"
      aria-hidden
    >
      <path d="M4 2.5v9l7-4.5z" />
    </svg>
  );
}

interface PipelineRowProps {
  pipeline: Pipeline;
}

function parseParams(
  raw: string,
): { ok: true; value?: Record<string, unknown> } | { ok: false } {
  if (raw.trim() === "") return { ok: true, value: undefined };
  try {
    const parsed = JSON.parse(raw);
    if (
      typeof parsed !== "object" ||
      parsed === null ||
      Array.isArray(parsed)
    ) {
      return { ok: false };
    }
    return { ok: true, value: parsed };
  } catch {
    return { ok: false };
  }
}

export function PipelineRow({ pipeline }: PipelineRowProps) {
  const navigate = useNavigate();
  const [expanded, setExpanded] = useState(false);
  const [paramsOpen, setParamsOpen] = useState(false);
  const [paramsRaw, setParamsRaw] = useState("");
  const panelId = useId();
  const queryClient = useQueryClient();

  const run = useMutation({
    mutationFn: () => {
      const parsed = parseParams(paramsRaw);
      return apiClient.runPipeline(
        pipeline.id,
        parsed.ok ? parsed.value : undefined,
      );
    },
    onSuccess: () => {
      setExpanded(true);
      setParamsOpen(false);
      setParamsRaw("");
      queryClient.invalidateQueries({
        queryKey: queryKeys.pipelineRuns(pipeline.id),
      });
    },
  });

  const paramsValid = parseParams(paramsRaw).ok;
  const stepCount = pipeline.steps.length;

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
            <span className="flex items-center gap-2">
              <span className="truncate font-medium text-[var(--swarm-ink)]">
                {pipeline.name}
              </span>
              <ScheduleChip pipelineId={pipeline.id} />
            </span>
            {pipeline.description && (
              <span className="block truncate text-xs text-[var(--swarm-muted)]">
                {pipeline.description}
              </span>
            )}
          </span>
        </button>

        <span className="shrink-0 tabular-nums text-sm text-[var(--swarm-muted)]">
          {stepCount} step{stepCount === 1 ? "" : "s"}
        </span>

        <button
          type="button"
          onClick={() => navigate(`/workflows/${pipeline.id}`)}
          className="inline-flex shrink-0 items-center rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-xs font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          View
        </button>

        <button
          type="button"
          onClick={() => {
            setExpanded(true);
            setParamsOpen(true);
          }}
          disabled={run.isPending}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-md bg-[var(--swarm-primary)] px-2.5 py-1.5 text-xs font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <IconPlay />
          {run.isPending ? "Starting…" : "Run"}
        </button>
      </div>

      {expanded && (
        <div id={panelId} className="space-y-5 px-4 pb-4 pl-11">
          {paramsOpen && (
            <section className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-3">
              <label
                htmlFor={`run-params-${pipeline.id}`}
                className="mb-1 block text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]"
              >
                Runtime params{" "}
                <span className="font-normal normal-case">
                  (JSON, optional)
                </span>
              </label>
              <textarea
                id={`run-params-${pipeline.id}`}
                value={paramsRaw}
                onChange={(e) => setParamsRaw(e.target.value)}
                rows={3}
                spellCheck={false}
                placeholder={'{ "since": "2026-01-01", "limit": 1000 }'}
                aria-invalid={!paramsValid || undefined}
                className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
              />
              <p className="mt-1 text-xs text-[var(--swarm-muted)]">
                Forwarded to every step's dispatch, resolved via{" "}
                <code className="font-mono">{"{param:key}"}</code> placeholders.
              </p>
              {!paramsValid && (
                <p className="mt-1 text-xs text-[var(--swarm-danger)]">
                  Runtime params must be a JSON object.
                </p>
              )}
              <div className="mt-2 flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => run.mutate()}
                  disabled={!paramsValid || run.isPending}
                  className="inline-flex items-center gap-1.5 rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  <IconPlay />
                  {run.isPending ? "Starting…" : "Start run"}
                </button>
                <button
                  type="button"
                  onClick={() => setParamsOpen(false)}
                  className="rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  Cancel
                </button>
              </div>
              {run.isError && (
                <p
                  className="mt-2 text-sm text-[var(--swarm-danger)]"
                  role="alert"
                >
                  Could not start run. Check that nodes matching the pipeline's
                  targets are online.
                </p>
              )}
            </section>
          )}

          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Steps
            </h3>
            <StepGraph steps={pipeline.steps} />
          </section>

          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Run history
            </h3>
            <RunHistory pipelineId={pipeline.id} />
          </section>

          <section>
            <h3 className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
              Schedules
            </h3>
            <SchedulesPanel pipelineId={pipeline.id} />
          </section>
        </div>
      )}
    </div>
  );
}

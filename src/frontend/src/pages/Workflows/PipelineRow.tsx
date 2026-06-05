import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useId, useState } from "react";
import { IconChevron } from "../../components/shell/icons";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { Pipeline } from "../../store/store";
import { RunHistory } from "./RunHistory";
import { ScheduleChip } from "./ScheduleChip";
import { StepGraph } from "./StepGraph";

function IconPlay() {
  return (
    <svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor" aria-hidden>
      <path d="M4 2.5v9l7-4.5z" />
    </svg>
  );
}

interface PipelineRowProps {
  pipeline: Pipeline;
}

export function PipelineRow({ pipeline }: PipelineRowProps) {
  const [expanded, setExpanded] = useState(false);
  const panelId = useId();
  const queryClient = useQueryClient();

  const run = useMutation({
    mutationFn: () => apiClient.runPipeline(pipeline.id),
    onSuccess: () => {
      setExpanded(true);
      queryClient.invalidateQueries({ queryKey: queryKeys.pipelineRuns(pipeline.id) });
    },
  });

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
          onClick={() => run.mutate()}
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
          {run.isError && (
            <p className="text-sm text-[var(--swarm-danger)]" role="alert">
              Could not start run. Check that nodes matching the pipeline's targets are online.
            </p>
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
        </div>
      )}
    </div>
  );
}

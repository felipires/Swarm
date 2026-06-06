import type { PipelineStep } from "../../store/store";
import { FAILURE_LABEL, STRATEGY_LABEL, toLevels } from "./pipelineGraph";

function stepTitle(step: PipelineStep): string {
  const strategy = step.strategyOverride
    ? STRATEGY_LABEL[step.strategyOverride]
    : "Inherited strategy";
  return `${strategy} · On failure: ${FAILURE_LABEL[step.failurePolicy]}`;
}

export function StepGraph({ steps }: { steps: PipelineStep[] }) {
  if (steps.length === 0) {
    return <p className="text-sm text-[var(--swarm-muted)]">No steps defined.</p>;
  }

  const levels = toLevels([...steps].sort((a, b) => a.order - b.order));

  return (
    <div className="flex flex-wrap items-stretch gap-1 overflow-x-auto">
      {levels.map((level, i) => (
        <div key={i} className="flex items-center gap-1">
          <div className="flex flex-col gap-1">
            {level.map((step) => (
              <span
                key={step.id}
                className="inline-flex items-center gap-1.5 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2 py-1 text-xs text-[var(--swarm-ink)]"
                title={stepTitle(step)}
              >
                <span className="font-mono text-[var(--swarm-muted)] tabular-nums">
                  {step.order}
                </span>
                {step.name}
              </span>
            ))}
          </div>
          {i < levels.length - 1 && (
            <span className="px-0.5 text-[var(--swarm-border-strong)]" aria-hidden>
              →
            </span>
          )}
        </div>
      ))}
    </div>
  );
}

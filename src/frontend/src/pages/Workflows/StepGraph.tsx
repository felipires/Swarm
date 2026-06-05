import type { PipelineStep } from "../../store/store";

/** Groups steps into dependency "levels" (topological layers) so the DAG renders
 *  left-to-right: roots first, then everything that depends only on prior levels.
 *  Falls back to declared order if a cycle or missing dependency is detected. */
function toLevels(steps: PipelineStep[]): PipelineStep[][] {
  const byName = new Map(steps.map((s) => [s.name, s]));
  const placed = new Set<string>();
  const levels: PipelineStep[][] = [];

  let guard = steps.length + 1;
  while (placed.size < steps.length && guard-- > 0) {
    const level = steps.filter(
      (s) =>
        !placed.has(s.name) &&
        s.dependsOn.every((d) => !byName.has(d) || placed.has(d)),
    );
    if (level.length === 0) break;
    level.forEach((s) => placed.add(s.name));
    levels.push(level);
  }

  const leftover = steps.filter((s) => !placed.has(s.name));
  if (leftover.length > 0) levels.push(leftover);
  return levels;
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
                title={`Strategy: ${step.strategy} · On failure: ${step.failurePolicy}`}
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

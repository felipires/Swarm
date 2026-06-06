import type { DispatchStrategy, FailurePolicy, PipelineStep } from "../../store/store";

export const STRATEGY_LABEL: Record<DispatchStrategy, string> = {
  SpecificNode: "Specific node",
  AllOnlineNodes: "All online nodes",
  AnyOnlineNode: "Any online node",
  TaggedNodes: "Tagged nodes",
};

export const FAILURE_LABEL: Record<FailurePolicy, string> = {
  FailPipeline: "Fail pipeline",
  ContinuePipeline: "Continue pipeline",
};

/** Groups steps into dependency layers (topological levels) keyed on step id.
 *  Roots first, then steps whose deps are all placed. Leftover (cycle / missing
 *  dep) is appended as a final layer so nothing is dropped. */
export function toLevels(steps: PipelineStep[]): PipelineStep[][] {
  const ids = new Set(steps.map((s) => s.id));
  const placed = new Set<string>();
  const levels: PipelineStep[][] = [];

  let guard = steps.length + 1;
  while (placed.size < steps.length && guard-- > 0) {
    const level = steps.filter(
      (s) =>
        !placed.has(s.id) &&
        s.dependsOn.every((d) => !ids.has(d) || placed.has(d)),
    );
    if (level.length === 0) break;
    level.forEach((s) => placed.add(s.id));
    levels.push(level);
  }

  const leftover = steps.filter((s) => !placed.has(s.id));
  if (leftover.length > 0) levels.push(leftover);
  return levels;
}

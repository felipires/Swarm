import type { StatusTone } from "../../../components/ui/StatusPill";
import type { PipelineStepInstanceStatus } from "../../../store/store";

export const STEP_STATUS_TONE: Record<PipelineStepInstanceStatus, StatusTone> = {
  Waiting: "muted",
  Dispatched: "info",
  Completed: "success",
  Failed: "danger",
  Skipped: "warning",
};

/** OKLCH token a node border/accent uses for a step status. */
export const STEP_STATUS_COLOR: Record<PipelineStepInstanceStatus, string> = {
  Waiting: "var(--swarm-border-strong)",
  Dispatched: "var(--swarm-info)",
  Completed: "var(--swarm-success)",
  Failed: "var(--swarm-danger)",
  Skipped: "var(--swarm-warning)",
};

export function isStepActive(status: PipelineStepInstanceStatus): boolean {
  return status === "Dispatched";
}

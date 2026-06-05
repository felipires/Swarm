import type { StatusTone } from "../../components/ui/StatusPill";
import type { PipelineRunStatus } from "../../store/store";

export const RUN_TONE: Record<PipelineRunStatus, StatusTone> = {
  Running: "info",
  Completed: "success",
  Failed: "danger",
  Cancelled: "muted",
};

export function isRunning(status: PipelineRunStatus): boolean {
  return status === "Running";
}

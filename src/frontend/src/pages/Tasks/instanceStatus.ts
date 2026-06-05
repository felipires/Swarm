import type { StatusTone } from "../../components/ui/StatusPill";
import type { TaskInstance } from "../../store/store";

type InstanceStatus = TaskInstance["status"];

export const INSTANCE_TONE: Record<InstanceStatus, StatusTone> = {
  Pending: "muted",
  Dispatched: "info",
  Running: "info",
  Completed: "success",
  Failed: "danger",
};

export function isInstanceActive(status: InstanceStatus): boolean {
  return status === "Running" || status === "Dispatched";
}

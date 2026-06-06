import type { StatusTone } from "../../components/ui/StatusPill";
import type { NodeHealth } from "../../store/store";

export const HEALTH_TONE: Record<NodeHealth, StatusTone> = {
  Healthy: "success",
  Degraded: "warning",
  Unhealthy: "danger",
};

/** Memory used as a percentage of (used + available). Null when unknown. */
export function memoryPercent(used: number, available: number): number | null {
  const total = used + available;
  return total > 0 ? (used / total) * 100 : null;
}

const UNITS = ["B", "KB", "MB", "GB", "TB", "PB"];

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return "0 B";
  const i = Math.min(UNITS.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / 1024 ** i;
  return `${value >= 100 || i === 0 ? Math.round(value) : value.toFixed(1)} ${UNITS[i]}`;
}

export function formatUptime(seconds: number): string {
  if (seconds < 60) return `${Math.floor(seconds)}s`;
  const m = Math.floor(seconds / 60);
  if (m < 60) return `${m}m`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h}h ${m % 60}m`;
  const d = Math.floor(h / 24);
  return `${d}d ${h % 24}h`;
}

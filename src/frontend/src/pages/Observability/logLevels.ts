import type { LogLevel } from "../../store/store";

export interface LevelStyle {
  label: string;
  abbr: string;
  color: string;
  bold?: boolean;
}

export const LEVEL_STYLE: Record<LogLevel, LevelStyle> = {
  Debug: { label: "Debug", abbr: "DBG", color: "var(--swarm-muted)" },
  Information: { label: "Info", abbr: "INF", color: "var(--swarm-ink)" },
  Warning: { label: "Warning", abbr: "WRN", color: "var(--swarm-warning)" },
  Error: { label: "Error", abbr: "ERR", color: "var(--swarm-danger)" },
  Critical: { label: "Critical", abbr: "CRT", color: "var(--swarm-danger)", bold: true },
};

/** Filter chip groups: each maps to the levels it admits. */
export const LEVEL_FILTERS = {
  All: null,
  Info: ["Debug", "Information"],
  Warning: ["Warning"],
  Error: ["Error", "Critical"],
} as const satisfies Record<string, LogLevel[] | null>;

export type LevelFilter = keyof typeof LEVEL_FILTERS;

export function passesFilter(level: LogLevel, filter: LevelFilter): boolean {
  const allowed = LEVEL_FILTERS[filter] as readonly LogLevel[] | null;
  return allowed === null || allowed.includes(level);
}

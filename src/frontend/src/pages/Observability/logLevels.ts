export interface LevelStyle {
  label: string;
  abbr: string;
  color: string;
  bold?: boolean;
}

const DEBUG: LevelStyle = { label: "Debug", abbr: "DBG", color: "var(--swarm-muted)" };
const INFO: LevelStyle = { label: "Info", abbr: "INF", color: "var(--swarm-ink)" };
const WARN: LevelStyle = { label: "Warning", abbr: "WRN", color: "var(--swarm-log-warning)" };
const ERROR: LevelStyle = { label: "Error", abbr: "ERR", color: "var(--swarm-danger)" };
const CRIT: LevelStyle = { label: "Critical", abbr: "CRT", color: "var(--swarm-danger)", bold: true };

/** Maps the worker's free-form level string (Serilog/MEL variants) to a style.
 *  Tolerant: unknown levels fall back to Info so a row never crashes. */
export function styleForLevel(level: string): LevelStyle {
  switch (level.toLowerCase()) {
    case "verbose":
    case "trace":
    case "debug":
      return DEBUG;
    case "information":
    case "info":
      return INFO;
    case "warning":
    case "warn":
      return WARN;
    case "error":
      return ERROR;
    case "fatal":
    case "critical":
      return CRIT;
    default:
      return INFO;
  }
}

/** Which broad bucket a raw level belongs to, for filtering. */
export type LevelBucket = "debug" | "info" | "warning" | "error";

function bucketOf(level: string): LevelBucket {
  const s = styleForLevel(level);
  if (s === DEBUG) return "debug";
  if (s === WARN) return "warning";
  if (s === ERROR || s === CRIT) return "error";
  return "info";
}

export const LEVEL_FILTERS = {
  All: null,
  Info: ["debug", "info"],
  Warning: ["warning"],
  Error: ["error"],
} as const satisfies Record<string, LevelBucket[] | null>;

export type LevelFilter = keyof typeof LEVEL_FILTERS;

export function passesFilter(level: string, filter: LevelFilter): boolean {
  const allowed = LEVEL_FILTERS[filter] as readonly LevelBucket[] | null;
  return allowed === null || allowed.includes(bucketOf(level));
}

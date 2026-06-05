const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: "auto", style: "short" });

const DIVISIONS: { amount: number; unit: Intl.RelativeTimeFormatUnit }[] = [
  { amount: 60, unit: "second" },
  { amount: 60, unit: "minute" },
  { amount: 24, unit: "hour" },
  { amount: 7, unit: "day" },
  { amount: 4.34524, unit: "week" },
  { amount: 12, unit: "month" },
  { amount: Number.POSITIVE_INFINITY, unit: "year" },
];

/** Compact relative time, e.g. "2 sec. ago", "4 min. ago". `now` lets callers
 *  drive a shared ticking clock instead of each cell reading Date.now(). */
export function relativeTime(iso: string, now: number = Date.now()): string {
  let duration = (new Date(iso).getTime() - now) / 1000;
  if (!Number.isFinite(duration)) return "—";

  for (const division of DIVISIONS) {
    if (Math.abs(duration) < division.amount) {
      return rtf.format(Math.round(duration), division.unit);
    }
    duration /= division.amount;
  }
  return "—";
}

const absoluteFmt = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "medium",
});

export function absoluteTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "—" : absoluteFmt.format(d);
}

/** Compact elapsed duration between two instants, e.g. "1m 12s", "3h 4m", "820ms".
 *  When `end` is omitted, measures to `now` (an in-progress run). */
export function duration(startIso: string, endIso?: string, now: number = Date.now()): string {
  const start = new Date(startIso).getTime();
  const end = endIso ? new Date(endIso).getTime() : now;
  if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return "—";

  const ms = end - start;
  if (ms < 1000) return `${ms}ms`;

  const totalSec = Math.floor(ms / 1000);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;

  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

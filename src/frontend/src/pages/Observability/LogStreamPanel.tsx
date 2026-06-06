import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { LogEntry } from "../../store/store";
import { passesFilter, styleForLevel, type LevelFilter } from "./logLevels";

const ROW_HEIGHT = 22;
const OVERSCAN = 8;

interface LogStreamPanelProps {
  entries: LogEntry[];
  filter: LevelFilter;
  query: string;
  autoScroll: boolean;
  onAutoScrollChange: (value: boolean) => void;
}

const timeFmt = new Intl.DateTimeFormat(undefined, {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hour12: false,
});

function logTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? "--:--:--" : timeFmt.format(d);
}

function LogRow({ entry, top }: { entry: LogEntry; top: number }) {
  const style = styleForLevel(entry.level);
  const message = entry.message;

  return (
    <div
      className="absolute left-0 right-0 flex items-center gap-3 px-3 font-mono text-xs"
      style={{ top, height: ROW_HEIGHT, lineHeight: `${ROW_HEIGHT}px` }}
    >
      <span className="shrink-0 tabular-nums text-[var(--swarm-muted)]">
        {logTime(entry.timestamp)}
      </span>
      <span
        className="w-8 shrink-0 font-medium"
        style={{ color: style.color, fontWeight: style.bold ? 700 : 600 }}
      >
        {style.abbr}
      </span>
      <span
        className="min-w-0 flex-1 truncate"
        style={{ color: style.color, fontWeight: style.bold ? 600 : 400 }}
        title={message}
      >
        {message}
      </span>
    </div>
  );
}

export function LogStreamPanel({
  entries,
  filter,
  query,
  autoScroll,
  onAutoScrollChange,
}: LogStreamPanelProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportH, setViewportH] = useState(0);

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    return entries.filter((e) => {
      if (!passesFilter(e.level, filter)) return false;
      if (!q) return true;
      const msg = e.message.toLowerCase();
      return msg.includes(q);
    });
  }, [entries, filter, query]);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => setViewportH(el.clientHeight));
    ro.observe(el);
    setViewportH(el.clientHeight);
    return () => ro.disconnect();
  }, []);

  // Keep pinned to bottom as new rows arrive, while auto-scroll is on.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (el && autoScroll) {
      el.scrollTop = el.scrollHeight;
    }
  }, [visible.length, autoScroll]);

  const total = visible.length;
  const totalHeight = total * ROW_HEIGHT;
  const start = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const end = Math.min(total, Math.ceil((scrollTop + viewportH) / ROW_HEIGHT) + OVERSCAN);
  const slice = visible.slice(start, end);

  const handleScroll = (e: React.UIEvent<HTMLDivElement>) => {
    const el = e.currentTarget;
    setScrollTop(el.scrollTop);
    // If the user scrolls away from the bottom, disengage auto-scroll;
    // re-engage when they return to within a row of the bottom.
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < ROW_HEIGHT;
    if (atBottom !== autoScroll) onAutoScrollChange(atBottom);
  };

  if (total === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
        {entries.length === 0
          ? "Waiting for log events…"
          : "No entries match the current filter."}
      </div>
    );
  }

  return (
    <div
      ref={scrollRef}
      onScroll={handleScroll}
      className="relative h-full overflow-y-auto bg-[var(--swarm-bg)]"
      role="log"
      aria-label="Log stream"
      tabIndex={0}
    >
      <div style={{ height: totalHeight, position: "relative" }}>
        {slice.map((entry, i) => (
          <LogRow key={entry.id} entry={entry} top={(start + i) * ROW_HEIGHT} />
        ))}
      </div>
    </div>
  );
}

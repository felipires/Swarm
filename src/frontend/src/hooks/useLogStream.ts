import { useEffect, useRef, useState } from "react";
import { apiClient } from "../services/api";
import type { LogEntry } from "../store/store";

export type StreamState = "idle" | "connecting" | "open" | "error";

const MAX_ENTRIES = 1000;

interface UseLogStreamResult {
  entries: LogEntry[];
  state: StreamState;
  clear: () => void;
}

/** Subscribes to the SSE log stream for a node while `live` is true. Keeps a
 *  bounded ring buffer (newest kept, oldest dropped) so the DOM never unbounded-
 *  grows. Pausing (live=false) closes the socket but retains the buffer. */
export function useLogStream(nodeId: string | null, live: boolean): UseLogStreamResult {
  const [entries, setEntries] = useState<LogEntry[]>([]);
  const [state, setState] = useState<StreamState>("idle");
  const seen = useRef<Set<string>>(new Set());

  // Reset the buffer when the selected node changes.
  useEffect(() => {
    setEntries([]);
    seen.current = new Set();
  }, [nodeId]);

  useEffect(() => {
    if (!nodeId || !live) {
      setState("idle");
      return;
    }

    setState("connecting");
    const source = new EventSource(apiClient.logStreamUrl(nodeId));

    source.onopen = () => setState("open");

    source.onmessage = (event) => {
      try {
        const entry = JSON.parse(event.data) as LogEntry;
        if (seen.current.has(entry.id)) return;
        seen.current.add(entry.id);
        setEntries((prev) => {
          const next = [...prev, entry];
          if (next.length > MAX_ENTRIES) {
            const dropped = next.splice(0, next.length - MAX_ENTRIES);
            dropped.forEach((d) => seen.current.delete(d.id));
          }
          return next;
        });
      } catch {
        // Ignore malformed frames rather than tearing down the stream.
      }
    };

    source.onerror = () => setState("error");

    return () => source.close();
  }, [nodeId, live]);

  const clear = () => {
    setEntries([]);
    seen.current = new Set();
  };

  return { entries, state, clear };
}

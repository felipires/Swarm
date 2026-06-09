import { useEffect, useState } from "react";

export type ToastTone = "error" | "success" | "info";

interface Toast {
  id: number;
  message: string;
  tone: ToastTone;
}

// Module-level emitter so non-React code (the QueryClient's MutationCache) can
// raise toasts without prop/context plumbing.
type Listener = (t: Toast) => void;
const listeners = new Set<Listener>();
let nextId = 1;

export function toast(message: string, tone: ToastTone = "info") {
  const t: Toast = { id: nextId++, message, tone };
  listeners.forEach((l) => l(t));
}

const TONE_CLASS: Record<ToastTone, string> = {
  error: "border-[var(--swarm-danger)]/40 bg-[var(--swarm-danger-subtle)] text-[var(--swarm-danger)]",
  success: "border-[var(--swarm-success)]/40 bg-[var(--swarm-success-subtle)] text-[var(--swarm-success)]",
  info: "border-[var(--swarm-border)] bg-[var(--swarm-surface)] text-[var(--swarm-ink)]",
};

const DISMISS_MS = 6000;

/** Fixed bottom-right stack of auto-dismissing toasts. Mount once near the root. */
export function ToastViewport() {
  const [toasts, setToasts] = useState<Toast[]>([]);

  useEffect(() => {
    const listener: Listener = (t) => {
      setToasts((prev) => [...prev, t]);
      window.setTimeout(() => {
        setToasts((prev) => prev.filter((x) => x.id !== t.id));
      }, DISMISS_MS);
    };
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  }, []);

  if (toasts.length === 0) return null;

  return (
    <div
      className="pointer-events-none fixed bottom-4 right-4 z-[1000] flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2"
      role="region"
      aria-label="Notifications"
    >
      {toasts.map((t) => (
        <div
          key={t.id}
          role={t.tone === "error" ? "alert" : "status"}
          className={`pointer-events-auto flex items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-md ${TONE_CLASS[t.tone]}`}
        >
          <span className="min-w-0 flex-1 break-words">{t.message}</span>
          <button
            type="button"
            onClick={() => setToasts((prev) => prev.filter((x) => x.id !== t.id))}
            aria-label="Dismiss"
            className="shrink-0 rounded px-1 opacity-70 transition-opacity hover:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-current"
          >
            ×
          </button>
        </div>
      ))}
    </div>
  );
}

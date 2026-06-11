import { useId, useState } from "react";

interface JsonViewerProps {
  /** Raw JSON string (or any string; pretty-printed when parseable). */
  value: string;
  label?: string;
  maxHeight?: string;
  /** Start collapsed. Defaults to false. */
  defaultCollapsed?: boolean;
}

function prettify(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export function JsonViewer({
  value,
  label,
  maxHeight = "16rem",
  defaultCollapsed = false,
}: JsonViewerProps) {
  const [copied, setCopied] = useState(false);
  const [collapsed, setCollapsed] = useState(defaultCollapsed);
  const bodyId = useId();
  const pretty = prettify(value);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(pretty);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard blocked; no-op rather than surfacing a hard error.
    }
  };

  return (
    <div className="overflow-hidden rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)]">
      <div className="flex items-center justify-between border-b border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5">
        <button
          type="button"
          onClick={() => setCollapsed((c) => !c)}
          aria-expanded={!collapsed}
          aria-controls={bodyId}
          className="flex items-center gap-1.5 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <svg
            width="10"
            height="10"
            viewBox="0 0 10 10"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden
            style={{
              transform: collapsed ? "none" : "rotate(90deg)",
              transitionDuration: "var(--swarm-duration)",
              transitionTimingFunction: "var(--swarm-ease-out)",
            }}
          >
            <path d="M3 2l4 3-4 3" />
          </svg>
          {label ?? "JSON"}
        </button>

        {!collapsed && (
          <button
            type="button"
            onClick={copy}
            className="rounded px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
            style={{ transitionDuration: "var(--swarm-duration)" }}
            aria-live="polite"
          >
            {copied ? "Copied" : "Copy"}
          </button>
        )}
      </div>

      {!collapsed && (
        <pre
          id={bodyId}
          className="overflow-auto px-3 py-2 font-mono text-xs leading-relaxed text-[var(--swarm-ink)]"
          style={{ maxHeight }}
        >
          {pretty}
        </pre>
      )}
    </div>
  );
}

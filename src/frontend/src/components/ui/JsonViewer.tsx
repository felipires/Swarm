import { useState } from "react";

interface JsonViewerProps {
  /** Raw JSON string (or any string; pretty-printed when parseable). */
  value: string;
  label?: string;
  maxHeight?: string;
}

function prettify(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}

export function JsonViewer({ value, label, maxHeight = "16rem" }: JsonViewerProps) {
  const [copied, setCopied] = useState(false);
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
        <span className="text-xs font-medium text-[var(--swarm-muted)]">
          {label ?? "JSON"}
        </span>
        <button
          type="button"
          onClick={copy}
          className="rounded px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
          aria-live="polite"
        >
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <pre
        className="overflow-auto px-3 py-2 font-mono text-xs leading-relaxed text-[var(--swarm-ink)]"
        style={{ maxHeight }}
      >
        {pretty}
      </pre>
    </div>
  );
}

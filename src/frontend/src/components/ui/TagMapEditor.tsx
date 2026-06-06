import { useState } from "react";

interface TagMapEditorProps {
  value: Record<string, string>;
  onChange: (next: Record<string, string>) => void;
  /** Label for the add-button; default "Add tag". */
  addLabel?: string;
}

const inputClass =
  "rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";

/** Edits a string→string map as removable chips plus a key=value add row.
 *  Used for tag selectors (dispatch + pipeline step targeting). */
export function TagMapEditor({ value, onChange, addLabel = "Add tag" }: TagMapEditorProps) {
  const [key, setKey] = useState("");
  const [val, setVal] = useState("");
  const entries = Object.entries(value);

  const add = () => {
    const k = key.trim();
    if (!k) return;
    onChange({ ...value, [k]: val.trim() });
    setKey("");
    setVal("");
  };

  const remove = (k: string) => {
    const next = { ...value };
    delete next[k];
    onChange(next);
  };

  return (
    <div className="space-y-2">
      {entries.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {entries.map(([k, v]) => (
            <span
              key={k}
              className="inline-flex items-center gap-1.5 rounded bg-[var(--swarm-primary-subtle)] py-0.5 pl-1.5 pr-1 font-mono text-xs text-[var(--swarm-ink)]"
            >
              {k}={v}
              <button
                type="button"
                onClick={() => remove(k)}
                aria-label={`Remove ${k}`}
                className="rounded px-0.5 text-[var(--swarm-muted)] transition-colors hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                style={{ transitionDuration: "var(--swarm-duration)" }}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <input
          value={key}
          onChange={(e) => setKey(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              add();
            }
          }}
          placeholder="region"
          aria-label="Tag key"
          className={`${inputClass} w-24 font-mono`}
        />
        <span className="text-[var(--swarm-muted)]">=</span>
        <input
          value={val}
          onChange={(e) => setVal(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              add();
            }
          }}
          placeholder="eu"
          aria-label="Tag value"
          className={`${inputClass} w-24 font-mono`}
        />
        <button
          type="button"
          onClick={add}
          disabled={!key.trim()}
          className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-2.5 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {addLabel}
        </button>
      </div>
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import { IconTrash } from "../shell/icons";

interface ConfirmDeleteButtonProps {
  onConfirm: () => void;
  label: string;
  /** Inline prompt shown after the first click. */
  prompt?: string;
  disabled?: boolean;
}

/** Two-step inline delete: the trash icon reveals a tokenized "Delete / Cancel"
 *  pair on first click, replacing the unstyleable native confirm(). Reverts on
 *  blur-out or Escape so it never traps the user mid-decision. */
export function ConfirmDeleteButton({
  onConfirm,
  label,
  prompt = "Delete?",
  disabled,
}: ConfirmDeleteButtonProps) {
  const [armed, setArmed] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!armed) return;
    confirmRef.current?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setArmed(false);
    };
    const onPointer = (e: PointerEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Element)) {
        setArmed(false);
      }
    };
    document.addEventListener("keydown", onKey);
    document.addEventListener("pointerdown", onPointer);
    return () => {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("pointerdown", onPointer);
    };
  }, [armed]);

  if (armed) {
    return (
      <div
        ref={wrapRef}
        className="inline-flex items-center gap-1"
        role="group"
        aria-label={prompt}
      >
        <button
          ref={confirmRef}
          type="button"
          onClick={() => {
            setArmed(false);
            onConfirm();
          }}
          className="rounded-md bg-[var(--swarm-danger)] px-2 py-1 text-xs font-medium text-[var(--swarm-on-primary)] transition-colors hover:opacity-90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Delete
        </button>
        <button
          type="button"
          onClick={() => setArmed(false)}
          className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Cancel
        </button>
      </div>
    );
  }

  return (
    <button
      type="button"
      onClick={() => setArmed(true)}
      disabled={disabled}
      aria-label={label}
      className="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-md text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-danger-subtle)] hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-40"
      style={{ transitionDuration: "var(--swarm-duration)" }}
    >
      <IconTrash width={15} height={15} />
    </button>
  );
}

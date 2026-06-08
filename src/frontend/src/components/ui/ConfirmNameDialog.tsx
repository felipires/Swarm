import { useEffect, useRef, useState } from "react";

interface ConfirmNameDialogProps {
  /** The exact name the user must type to confirm. */
  name: string;
  title: string;
  /** Body shown above the input — explain consequences here. */
  children?: React.ReactNode;
  confirmLabel?: string;
  pending?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

/** A destructive type-to-confirm modal: the confirm button stays disabled until
 *  the user types the entity's exact name. Uses the native <dialog> top layer
 *  so it escapes any stacking/overflow context. */
export function ConfirmNameDialog({
  name,
  title,
  children,
  confirmLabel = "Delete",
  pending,
  onConfirm,
  onClose,
}: ConfirmNameDialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const [typed, setTyped] = useState("");

  useEffect(() => {
    const dlg = ref.current;
    if (dlg && !dlg.open) dlg.showModal();
  }, []);

  const matches = typed === name;

  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onCancel={onClose}
      className="m-auto w-[28rem] max-w-[calc(100vw-2rem)] rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-0 text-[var(--swarm-ink)] backdrop:bg-black/40"
    >
      <form
        method="dialog"
        onSubmit={(e) => {
          e.preventDefault();
          if (matches && !pending) onConfirm();
        }}
        className="flex flex-col gap-4 p-5"
      >
        <h2 className="text-base font-semibold text-[var(--swarm-ink)]">{title}</h2>
        {children && <div className="text-sm text-[var(--swarm-muted)]">{children}</div>}

        <div>
          <label htmlFor="confirm-name" className="mb-1 block text-sm text-[var(--swarm-muted)]">
            Type <span className="font-mono font-medium text-[var(--swarm-ink)]">{name}</span> to confirm
          </label>
          <input
            id="confirm-name"
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            autoFocus
            autoComplete="off"
            spellCheck={false}
            className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-danger)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-danger)]/25"
          />
        </div>

        <div className="flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={!matches || pending}
            className="rounded-md bg-[var(--swarm-danger)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:opacity-90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            {pending ? "Deleting…" : confirmLabel}
          </button>
        </div>
      </form>
    </dialog>
  );
}

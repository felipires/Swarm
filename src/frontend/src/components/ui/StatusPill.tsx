export type StatusTone = "success" | "warning" | "danger" | "info" | "muted";

const TONE_CLASS: Record<StatusTone, string> = {
  success: "bg-[var(--swarm-success-subtle)] text-[var(--swarm-success)]",
  warning: "bg-[var(--swarm-warning-subtle)] text-[var(--swarm-warning)]",
  danger: "bg-[var(--swarm-danger-subtle)] text-[var(--swarm-danger)]",
  info: "bg-[var(--swarm-surface-raised)] text-[var(--swarm-info)]",
  muted: "bg-[var(--swarm-surface-raised)] text-[var(--swarm-muted)]",
};

interface StatusPillProps {
  tone: StatusTone;
  label: string;
  /** Animate the dot for in-progress states. */
  pulsing?: boolean;
}

export function StatusPill({ tone, label, pulsing }: StatusPillProps) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${TONE_CLASS[tone]}`}
    >
      <span
        className={`h-1.5 w-1.5 rounded-full bg-current ${pulsing ? "animate-pulse motion-reduce:animate-none" : ""}`}
        aria-hidden
      />
      {label}
    </span>
  );
}

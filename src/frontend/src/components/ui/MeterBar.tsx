interface MeterBarProps {
  /** 0–100. */
  value: number;
  /** Width class; the track fills its container by default. */
  className?: string;
  /** Show the numeric percent before the bar. */
  showValue?: boolean;
  "aria-label"?: string;
}

/** Tints the fill by load: calm below 70%, amber 70–89%, danger 90%+. Color is
 *  paired with the always-visible percentage so it's never the sole signal. */
function toneFor(value: number): string {
  if (value >= 90) return "var(--swarm-danger)";
  if (value >= 70) return "var(--swarm-warning)";
  return "var(--swarm-success)";
}

export function MeterBar({ value, className, showValue, ...rest }: MeterBarProps) {
  const clamped = Math.max(0, Math.min(100, value));
  return (
    <span className={`inline-flex items-center gap-2 ${className ?? ""}`}>
      {showValue && (
        <span className="w-9 shrink-0 text-right text-xs tabular-nums text-[var(--swarm-ink)]">
          {Math.round(clamped)}%
        </span>
      )}
      <span
        role="meter"
        aria-valuenow={Math.round(clamped)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={rest["aria-label"]}
        className="relative h-1.5 min-w-[3rem] flex-1 overflow-hidden rounded-full bg-[var(--swarm-surface-raised)]"
      >
        <span
          className="absolute inset-y-0 left-0 rounded-full"
          style={{
            width: `${clamped}%`,
            background: toneFor(clamped),
            transition: "width var(--swarm-duration) var(--swarm-ease-out)",
          }}
        />
      </span>
    </span>
  );
}

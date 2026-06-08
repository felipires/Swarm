interface VersionBadgeProps {
  version: number;
  title?: string;
}

/** Small "v3" chip for the current version of a task/pipeline. */
export function VersionBadge({ version, title }: VersionBadgeProps) {
  return (
    <span
      title={title ?? `Version ${version}`}
      className="inline-flex shrink-0 items-center rounded-full bg-[var(--swarm-surface-raised)] px-2 py-0.5 text-xs font-medium tabular-nums text-[var(--swarm-muted)]"
    >
      v{version}
    </span>
  );
}

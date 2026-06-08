import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { apiClient } from "../../services/api";
import type { EntityVersionEntry } from "../../store/store";
import { absoluteTime, relativeTime } from "../../utils/time";

interface VersionHistoryProps {
  /** "task" | "pipeline" — drives which client methods are used. */
  kind: "task" | "pipeline";
  entityId: string;
  currentVersion?: number;
  /** Query key for this entity's version list (for invalidation). */
  versionsKey: readonly unknown[];
  /** Called after a successful restore so the parent can refetch the entity. */
  onRestored?: () => void;
  /** Render a fetched snapshot (entity-specific: JSON for tasks, canvas for pipelines). */
  renderSnapshot: (snapshot: unknown) => ReactNode;
  now: number;
}

export function VersionHistory({
  kind,
  entityId,
  currentVersion,
  versionsKey,
  onRestored,
  renderSnapshot,
  now,
}: VersionHistoryProps) {
  const queryClient = useQueryClient();
  const [viewing, setViewing] = useState<number | null>(null);
  const [compareWith, setCompareWith] = useState<number | null>(null);

  const listFn = () =>
    kind === "task" ? apiClient.getTaskVersions(entityId) : apiClient.getPipelineVersions(entityId);
  const getFn = (v: number) =>
    kind === "task" ? apiClient.getTaskVersion(entityId, v) : apiClient.getPipelineVersion(entityId, v);
  const restoreFn = (v: number): Promise<unknown> =>
    kind === "task" ? apiClient.restoreTaskVersion(entityId, v) : apiClient.restorePipelineVersion(entityId, v);

  const versionsQuery = useQuery({ queryKey: versionsKey, queryFn: listFn });

  const viewQuery = useQuery({
    queryKey: [...versionsKey, "snapshot", viewing],
    queryFn: () => getFn(viewing!),
    enabled: viewing !== null,
  });
  const compareQuery = useQuery({
    queryKey: [...versionsKey, "snapshot", compareWith],
    queryFn: () => getFn(compareWith!),
    enabled: compareWith !== null,
  });

  const restore = useMutation({
    mutationFn: (v: number) => restoreFn(v),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: versionsKey });
      onRestored?.();
    },
  });

  const versions = versionsQuery.data ?? [];

  if (versionsQuery.isLoading) {
    return (
      <div className="h-8 w-full animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
    );
  }
  if (versions.length === 0) {
    return <p className="text-sm text-[var(--swarm-muted)]">No version history yet.</p>;
  }

  return (
    <div className="space-y-3">
      <ul className="divide-y divide-[var(--swarm-border)]">
        {versions.map((v: EntityVersionEntry) => {
          const isCurrent = v.version === currentVersion;
          const isViewing = v.version === viewing;
          return (
            <li key={v.version} className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2 text-sm">
              <span className="font-medium tabular-nums text-[var(--swarm-ink)]">v{v.version}</span>
              {isCurrent && (
                <span className="rounded-full bg-[var(--swarm-primary-subtle)] px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-ink)]">
                  current
                </span>
              )}
              <span className="text-xs text-[var(--swarm-muted)]" title={absoluteTime(v.createdAt)}>
                {relativeTime(v.createdAt, now)}
              </span>
              <div className="ml-auto flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => setViewing(isViewing ? null : v.version)}
                  className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                  style={{ transitionDuration: "var(--swarm-duration)" }}
                >
                  {isViewing ? "Hide" : "View"}
                </button>
                {viewing !== null && viewing !== v.version && (
                  <button
                    type="button"
                    onClick={() => setCompareWith(compareWith === v.version ? null : v.version)}
                    className={`rounded-md px-2 py-1 text-xs font-medium transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)] ${
                      compareWith === v.version
                        ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
                        : "text-[var(--swarm-muted)] hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)]"
                    }`}
                    style={{ transitionDuration: "var(--swarm-duration)" }}
                  >
                    Compare
                  </button>
                )}
                {!isCurrent && (
                  <button
                    type="button"
                    onClick={() => {
                      if (window.confirm(`Restore v${v.version} as a new version?`)) restore.mutate(v.version);
                    }}
                    disabled={restore.isPending}
                    className="rounded-md px-2 py-1 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-50"
                    style={{ transitionDuration: "var(--swarm-duration)" }}
                  >
                    Restore
                  </button>
                )}
              </div>
            </li>
          );
        })}
      </ul>

      {restore.isError && (
        <p role="alert" className="text-sm text-[var(--swarm-danger)]">
          Could not restore that version.
        </p>
      )}

      {viewing !== null && (
        <div className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] p-3">
          {compareWith !== null ? (
            <div className="grid gap-3 lg:grid-cols-2">
              <div>
                <p className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
                  v{viewing}
                </p>
                {viewQuery.data?.snapshot ? renderSnapshot(viewQuery.data.snapshot) : <SnapshotSkeleton />}
              </div>
              <div>
                <p className="mb-2 text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
                  v{compareWith}
                </p>
                {compareQuery.data?.snapshot ? renderSnapshot(compareQuery.data.snapshot) : <SnapshotSkeleton />}
              </div>
            </div>
          ) : viewQuery.data?.snapshot ? (
            renderSnapshot(viewQuery.data.snapshot)
          ) : (
            <SnapshotSkeleton />
          )}
        </div>
      )}
    </div>
  );
}

const SnapshotSkeleton = () => (
  <div className="h-20 animate-pulse rounded bg-[var(--swarm-surface-raised)] motion-reduce:animate-none" />
);

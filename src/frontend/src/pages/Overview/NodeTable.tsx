import { useId, useState } from "react";
import { IconChevron } from "../../components/shell/icons";
import { ConfirmNameDialog } from "../../components/ui/ConfirmNameDialog";
import { MeterBar } from "../../components/ui/MeterBar";
import { StatusPill, type StatusTone } from "../../components/ui/StatusPill";
import { isStale } from "../../hooks/useClusterPulse";
import type { Node } from "../../store/store";
import { relativeTime } from "../../utils/time";
import { HEALTH_TONE, memoryPercent } from "./nodeMetrics";
import { NodeManagePanel } from "./NodeManagePanel";

type NodeState = "online" | "stale" | "offline";

const NODE_STATE: Record<NodeState, { label: string; tone: StatusTone }> = {
  online: { label: "Online", tone: "success" },
  stale: { label: "Stale", tone: "warning" },
  offline: { label: "Offline", tone: "danger" },
};

function nodeState(node: Node, now: number): NodeState {
  if (node.status === "Offline") return "offline";
  return isStale(node, now) ? "stale" : "online";
}

function TagChips({ tags }: { tags?: Record<string, string> }) {
  const entries = tags ? Object.entries(tags) : [];
  if (entries.length === 0) {
    return <span className="text-[var(--swarm-placeholder)]">—</span>;
  }
  const shown = entries.slice(0, 3);
  const extra = entries.length - shown.length;

  return (
    <div className="flex flex-wrap items-center gap-1">
      {shown.map(([k, v]) => (
        <span
          key={k}
          className="rounded bg-[var(--swarm-primary-subtle)] px-1.5 py-0.5 font-mono text-xs text-[var(--swarm-ink)]"
        >
          {k}={v}
        </span>
      ))}
      {extra > 0 && (
        <span
          className="text-xs text-[var(--swarm-muted)] tabular-nums"
          title={entries
            .slice(3)
            .map(([k, v]) => `${k}=${v}`)
            .join(", ")}
        >
          +{extra}
        </span>
      )}
    </div>
  );
}

interface NodeRowProps {
  node: Node;
  now: number;
  onDelete: (node: Node) => void;
  deleting: boolean;
}

function NodeRow({ node, now, onDelete, deleting }: NodeRowProps) {
  const state = nodeState(node, now);
  const [expanded, setExpanded] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const panelId = useId();
  const metrics = node.latestMetrics ?? null;
  const memPct = metrics
    ? memoryPercent(metrics.memoryUsedBytes, metrics.memoryAvailableBytes)
    : null;

  return (
    <>
      <tr className="border-t border-[var(--swarm-border)] transition-colors hover:bg-[var(--swarm-surface-raised)]">
        <td className="px-3 py-2 font-medium text-[var(--swarm-ink)]">
          <button
            type="button"
            onClick={() => setExpanded((e) => !e)}
            aria-expanded={expanded}
            aria-controls={panelId}
            className="flex w-full items-center gap-2 rounded-md text-left focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          >
            <span
              className="shrink-0 text-[var(--swarm-muted)] transition-transform"
              style={{
                transform: expanded ? "rotate(90deg)" : "none",
                transitionDuration: "var(--swarm-duration)",
                transitionTimingFunction: "var(--swarm-ease-out)",
              }}
              aria-hidden
            >
              <IconChevron direction="right" width={14} height={14} />
            </span>
            <span className="truncate" title={node.name}>
              {node.name}
            </span>
          </button>
        </td>
        <td className="px-3 py-2">
          <StatusPill
            tone={NODE_STATE[state].tone}
            label={NODE_STATE[state].label}
            pulsing={state === "stale"}
          />
        </td>
        <td className="px-3 py-2">
          {metrics ? (
            <StatusPill tone={HEALTH_TONE[metrics.health]} label={metrics.health} />
          ) : (
            <span className="text-[var(--swarm-placeholder)]">—</span>
          )}
        </td>
        <td className="px-3 py-2">
          {metrics ? (
            <MeterBar value={metrics.cpuPercent} showValue aria-label={`${node.name} CPU`} />
          ) : (
            <span className="text-[var(--swarm-placeholder)]">—</span>
          )}
        </td>
        <td className="px-3 py-2">
          {metrics && memPct !== null ? (
            <MeterBar value={memPct} showValue aria-label={`${node.name} memory`} />
          ) : (
            <span className="text-[var(--swarm-placeholder)]">—</span>
          )}
        </td>
        <td className="px-3 py-2 text-right tabular-nums text-[var(--swarm-ink)]">
          {metrics ? metrics.inFlightTasks : <span className="text-[var(--swarm-placeholder)]">—</span>}
        </td>
        <td
          className="px-3 py-2 tabular-nums text-[var(--swarm-muted)]"
          title={node.lastHeartbeatAt}
        >
          {relativeTime(node.lastHeartbeatAt, now)}
        </td>
        <td className="px-3 py-2">
          <TagChips tags={node.effectiveTags} />
        </td>
        <td className="px-3 py-2 text-right">
          <button
            type="button"
            onClick={() => setConfirming(true)}
            disabled={deleting}
            aria-label={`Remove node ${node.name}`}
            className="inline-flex h-9 w-9 items-center justify-center rounded-md text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-danger-subtle)] hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-40"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            <svg width="15" height="15" viewBox="0 0 18 18" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
              <path d="M3.5 5h11M7 5V3.5h4V5M5 5l.6 9a1 1 0 001 1h4.8a1 1 0 001-1L13 5" />
              <path d="M7.5 7.5v5M10.5 7.5v5" />
            </svg>
          </button>
        </td>
      </tr>
      {confirming && (
        <tr>
          <td className="p-0">
            <ConfirmNameDialog
              name={node.name}
              title="Remove node"
              confirmLabel="Remove node"
              pending={deleting}
              onConfirm={() => {
                onDelete(node);
                setConfirming(false);
              }}
              onClose={() => setConfirming(false)}
            >
              This permanently removes the node and its tags, capabilities, and
              pending env operations. This cannot be undone. (The worker process keeps
              running; it re-registers on its next heartbeat.)
            </ConfirmNameDialog>
          </td>
        </tr>
      )}
      {expanded && (
        <tr>
          <td colSpan={9} className="p-0" id={panelId}>
            <NodeManagePanel node={node} />
          </td>
        </tr>
      )}
    </>
  );
}

interface NodeTableProps {
  nodes: Node[];
  now: number;
  onDelete: (node: Node) => Promise<void>;
}

export function NodeTable({ nodes, now, onDelete }: NodeTableProps) {
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const handleDelete = async (node: Node) => {
    setDeletingId(node.id);
    try {
      await onDelete(node);
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <div className="overflow-x-auto rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]">
      <table className="w-full min-w-[56rem] table-fixed text-sm">
        <colgroup>
          <col className="w-44" />
          <col className="w-24" />
          <col className="w-24" />
          <col className="w-32" />
          <col className="w-32" />
          <col className="w-16" />
          <col className="w-28" />
          <col className="w-48" />
          <col className="w-16" />
        </colgroup>
        <thead>
          <tr className="text-left text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
            <th className="px-3 py-2 font-medium">Name</th>
            <th className="px-3 py-2 font-medium">Status</th>
            <th className="px-3 py-2 font-medium">Health</th>
            <th className="px-3 py-2 font-medium">CPU</th>
            <th className="px-3 py-2 font-medium">Memory</th>
            <th className="px-3 py-2 text-right font-medium">Tasks</th>
            <th className="px-3 py-2 font-medium">Heartbeat</th>
            <th className="px-3 py-2 font-medium">Tags</th>
            <th className="px-3 py-2 font-medium">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {nodes.map((node) => (
            <NodeRow
              key={node.id}
              node={node}
              now={now}
              onDelete={handleDelete}
              deleting={deletingId === node.id}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

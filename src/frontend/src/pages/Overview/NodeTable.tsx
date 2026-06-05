import { useState } from "react";
import { IconTrash } from "../../components/shell/icons";
import { StatusPill, type StatusTone } from "../../components/ui/StatusPill";
import { isStale } from "../../hooks/useClusterPulse";
import type { Node } from "../../store/store";
import { relativeTime } from "../../utils/time";

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

function Capabilities({ capabilities }: { capabilities?: string[] }) {
  if (!capabilities || capabilities.length === 0) {
    return <span className="text-[var(--swarm-placeholder)]">—</span>;
  }
  return (
    <span className="font-mono text-xs text-[var(--swarm-muted)]" title={capabilities.join(", ")}>
      {capabilities.join(", ")}
    </span>
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

  return (
    <tr className="border-t border-[var(--swarm-border)] transition-colors hover:bg-[var(--swarm-surface-raised)]">
      <td className="truncate px-3 py-2 font-medium text-[var(--swarm-ink)]" title={node.name}>
        {node.name}
      </td>
      <td className="px-3 py-2">
        <StatusPill
          tone={NODE_STATE[state].tone}
          label={NODE_STATE[state].label}
          pulsing={state === "stale"}
        />
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
      <td className="px-3 py-2">
        <Capabilities capabilities={node.capabilities} />
      </td>
      <td className="px-3 py-2 text-right">
        <button
          type="button"
          onClick={() => onDelete(node)}
          disabled={deleting}
          aria-label={`Remove node ${node.name}`}
          className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-danger-subtle)] hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-40"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          <IconTrash width={15} height={15} />
        </button>
      </td>
    </tr>
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
    const ok = window.confirm(
      `Remove node "${node.name}" from the cluster? This does not stop the worker process.`,
    );
    if (!ok) return;
    setDeletingId(node.id);
    try {
      await onDelete(node);
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <div className="overflow-hidden rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)]">
      <table className="w-full table-fixed text-sm">
        <colgroup>
          <col />
          <col className="w-28" />
          <col className="w-32" />
          <col className="w-56" />
          <col className="w-48" />
          <col className="w-12" />
        </colgroup>
        <thead>
          <tr className="text-left text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]">
            <th className="px-3 py-2 font-medium">Name</th>
            <th className="px-3 py-2 font-medium">Status</th>
            <th className="px-3 py-2 font-medium">Last heartbeat</th>
            <th className="px-3 py-2 font-medium">Tags</th>
            <th className="px-3 py-2 font-medium">Capabilities</th>
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

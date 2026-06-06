import { Handle, Position, type NodeProps } from "@xyflow/react";
import { STRATEGY_LABEL } from "../pipelineGraph";
import type { StepFlowNode } from "./graphToDraft";

const HANDLE_STYLE = {
  width: 9,
  height: 9,
  background: "var(--swarm-surface)",
  border: "1.5px solid var(--swarm-border-strong)",
};

export function StepNode({ data, selected }: NodeProps<StepFlowNode>) {
  const hasTask = Boolean(data.taskDefinitionId);
  const name = data.name.trim() || "Untitled step";

  return (
    <div
      className="w-48 rounded-lg border bg-[var(--swarm-surface)] px-3 py-2 shadow-sm"
      style={{
        borderColor: selected ? "var(--swarm-primary)" : "var(--swarm-border)",
        boxShadow: selected ? "0 0 0 1px var(--swarm-primary)" : "0 1px 2px var(--swarm-shadow)",
      }}
    >
      <Handle type="target" position={Position.Left} style={HANDLE_STYLE} />

      <p className="truncate text-sm font-medium text-[var(--swarm-ink)]" title={name}>
        {name}
      </p>
      <p className="mt-0.5 truncate text-xs text-[var(--swarm-muted)]">
        {data.strategy ? STRATEGY_LABEL[data.strategy] : "Inherited strategy"}
      </p>
      {!hasTask && (
        <p className="mt-1 text-xs font-medium text-[var(--swarm-warning)]">No task selected</p>
      )}

      <Handle type="source" position={Position.Right} style={HANDLE_STYLE} />
    </div>
  );
}

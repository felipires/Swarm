import {
  Background,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  type Edge,
  type Node as FlowNode,
  type NodeProps,
} from "@xyflow/react";
import { useMemo } from "react";
import type {
  Pipeline,
  PipelineStep,
  PipelineStepInstance,
  TaskDefinition,
} from "../../../store/store";
import { FAILURE_LABEL, STRATEGY_LABEL, toLevels } from "../pipelineGraph";
import { STEP_STATUS_COLOR } from "./stepStatus";
// @ts-ignore
import "@xyflow/react/dist/style.css";

const COL_GAP = 240;
const ROW_GAP = 110;

interface StepNodeData {
  step: PipelineStep;
  taskName: string;
  status: PipelineStepInstance["status"] | null;
  selected: boolean;
  [key: string]: unknown;
}

type StepFlowNode = FlowNode<StepNodeData>;

const HANDLE_STYLE = {
  width: 9,
  height: 9,
  background: "var(--swarm-surface)",
  border: "1.5px solid var(--swarm-border-strong)",
};

function ReadStepNode({ data }: NodeProps<StepFlowNode>) {
  const { step, taskName, status, selected } = data;
  const strategy = step.strategyOverride
    ? STRATEGY_LABEL[step.strategyOverride]
    : "Inherited strategy";

  const accent = status ? STEP_STATUS_COLOR[status] : null;
  const borderColor = selected
    ? "var(--swarm-primary)"
    : (accent ?? "var(--swarm-border)");

  return (
    <div
      className="w-52 cursor-pointer rounded-lg border bg-[var(--swarm-surface)] px-3 py-2 shadow-sm"
      style={{
        borderColor,
        boxShadow: selected
          ? "0 0 0 1px var(--swarm-primary)"
          : accent
            ? `0 0 0 1px ${accent}`
            : "0 1px 2px var(--swarm-shadow)",
      }}
    >
      <Handle type="target" position={Position.Left} style={HANDLE_STYLE} />
      <div className="flex items-center justify-between gap-2">
        <p
          className="truncate text-sm font-medium text-[var(--swarm-ink)]"
          title={step.name}
        >
          {step.name}
        </p>
        {status && (
          <span
            className="h-1.5 w-1.5 shrink-0 rounded-full"
            style={{ background: accent ?? "transparent" }}
            aria-hidden
          />
        )}
      </div>
      <p
        className="mt-0.5 truncate font-mono text-xs text-[var(--swarm-muted)]"
        title={taskName}
      >
        {taskName}
      </p>
      <p className="mt-1 truncate text-xs text-[var(--swarm-muted)]">
        {status ? status : `${strategy} · ${FAILURE_LABEL[step.failurePolicy]}`}
      </p>
      <Handle type="source" position={Position.Right} style={HANDLE_STYLE} />
    </div>
  );
}

const nodeTypes = { readStep: ReadStepNode };

const edgeOptions = {
  type: "smoothstep" as const,
  markerEnd: { type: MarkerType.ArrowClosed },
  style: { stroke: "var(--swarm-border-strong)" },
};

interface PipelineCanvasProps {
  pipeline: Pipeline;
  tasks: TaskDefinition[];
  /** Step-instance status by pipelineStepId, for the selected run. */
  stepStatusById: Map<string, PipelineStepInstance>;
  selectedStepId: string | null;
  onSelectStep: (stepId: string) => void;
}

export function PipelineCanvas({
  pipeline,
  tasks,
  stepStatusById,
  selectedStepId,
  onSelectStep,
}: PipelineCanvasProps) {
  const taskName = useMemo(() => {
    const byId = new Map(tasks.map((t) => [t.id, t.name]));
    return (id: string) => byId.get(id) ?? `${id.slice(0, 8)}…`;
  }, [tasks]);

  const { nodes, edges } = useMemo(() => {
    const sorted = [...pipeline.steps].sort((a, b) => a.order - b.order);
    const levels = toLevels(sorted);

    const flowNodes: StepFlowNode[] = [];
    levels.forEach((level, col) => {
      level.forEach((step, row) => {
        const offset = ((level.length - 1) / 2) * ROW_GAP;
        flowNodes.push({
          id: step.id,
          type: "readStep",
          position: { x: col * COL_GAP, y: row * ROW_GAP - offset },
          data: {
            step,
            taskName: taskName(step.taskDefinitionId),
            status: stepStatusById.get(step.id)?.status ?? null,
            selected: step.id === selectedStepId,
          },
          draggable: true,
        });
      });
    });

    const flowEdges: Edge[] = [];
    for (const step of sorted) {
      for (const dep of step.dependsOn) {
        flowEdges.push({
          id: `${dep}->${step.id}`,
          source: dep,
          target: step.id,
          ...edgeOptions,
        });
      }
    }

    return { nodes: flowNodes, edges: flowEdges };
  }, [pipeline.steps, taskName, stepStatusById, selectedStepId]);

  if (pipeline.steps.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
        This pipeline has no steps.
      </div>
    );
  }

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      onNodeClick={(_, node) => onSelectStep(node.id)}
      fitView
      nodesConnectable={false}
      edgesFocusable={false}
      proOptions={{ hideAttribution: true }}
    >
      <Background color="var(--swarm-border)" gap={20} />
    </ReactFlow>
  );
}

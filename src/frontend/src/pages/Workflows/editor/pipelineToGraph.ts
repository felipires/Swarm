import { MarkerType, type Edge } from "@xyflow/react";
import type { OutputMapping, Pipeline } from "../../../store/store";
import { toLevels } from "../pipelineGraph";
import type { StepFlowNode } from "./graphToDraft";

const COL_GAP = 240;
const ROW_GAP = 120;

const edgeOptions = {
  type: "smoothstep" as const,
  markerEnd: { type: MarkerType.ArrowClosed },
  style: { stroke: "var(--swarm-border-strong)" },
};

function parseTags(json?: string | null): Record<string, string> {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json);
    return typeof parsed === "object" && parsed !== null ? parsed : {};
  } catch {
    return {};
  }
}

/** Convert a saved pipeline (read model) into editor nodes + edges so the canvas
 *  can load an existing pipeline for editing. Node id = step id; edges encode
 *  the dependsOn (id→id) graph; graphToSteps later re-resolves deps by name. */
export function pipelineToGraph(pipeline: Pipeline): { nodes: StepFlowNode[]; edges: Edge[] } {
  const sorted = [...pipeline.steps].sort((a, b) => a.order - b.order);
  const levels = toLevels(sorted);

  const nodes: StepFlowNode[] = [];
  levels.forEach((level, col) => {
    level.forEach((step, row) => {
      const offset = ((level.length - 1) / 2) * ROW_GAP;
      nodes.push({
        id: step.id,
        type: "step",
        position: { x: col * COL_GAP, y: row * ROW_GAP - offset },
        data: {
          name: step.name,
          taskDefinitionId: step.taskDefinitionId,
          strategy: step.strategyOverride ?? null,
          targetNodeId: step.targetNodeId ?? null,
          targetTags: parseTags(step.targetTagsJson),
          failurePolicy: step.failurePolicy,
          outputMappings: (step.outputMappings ?? []) as OutputMapping[],
          runtimeParamsRaw: step.runtimeParamsJson ?? "",
        },
      });
    });
  });

  const edges: Edge[] = [];
  for (const step of sorted) {
    for (const dep of step.dependsOn) {
      edges.push({ id: `${dep}->${step.id}`, source: dep, target: step.id, ...edgeOptions });
    }
  }

  return { nodes, edges };
}

import type { Edge, Node as FlowNode } from "@xyflow/react";
import type {
  DispatchStrategy,
  DraftPipelineStep,
  FailurePolicy,
  OutputMapping,
} from "../../../store/store";

export interface StepNodeData {
  name: string;
  taskDefinitionId: string | null;
  strategy: DispatchStrategy | null;
  /** Set when strategy === "SpecificNode". */
  targetNodeId: string | null;
  /** Set when strategy === "TaggedNodes". */
  targetTags: Record<string, string>;
  failurePolicy: FailurePolicy;
  /** P1-8: extract upstream results into this step's runtime params. */
  outputMappings: OutputMapping[];
  /** P1-9: literal per-step params, edited as raw JSON text. */
  runtimeParamsRaw: string;
  [key: string]: unknown;
}

export type StepFlowNode = FlowNode<StepNodeData>;

export interface ValidationResult {
  ok: boolean;
  errors: string[];
}

/** Detects whether adding edge source→target would create a cycle, by checking
 *  if target can already reach source through existing edges. */
export function wouldCycle(edges: Edge[], source: string, target: string): boolean {
  if (source === target) return true;
  const adjacency = new Map<string, string[]>();
  for (const e of edges) {
    const list = adjacency.get(e.source) ?? [];
    list.push(e.target);
    adjacency.set(e.source, list);
  }
  // Can we reach `source` starting from `target`? If so, source→target closes a loop.
  const stack = [target];
  const seen = new Set<string>();
  while (stack.length) {
    const cur = stack.pop()!;
    if (cur === source) return true;
    if (seen.has(cur)) continue;
    seen.add(cur);
    stack.push(...(adjacency.get(cur) ?? []));
  }
  return false;
}

/** Parses a step's raw params text. ok=true with undefined value means "empty",
 *  ok=false means malformed (not a JSON object). */
export function parseStepParams(
  raw: string,
): { ok: true; value?: Record<string, unknown> } | { ok: false } {
  if (!raw || raw.trim() === "") return { ok: true, value: undefined };
  try {
    const parsed = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return { ok: false };
    return { ok: true, value: parsed };
  } catch {
    return { ok: false };
  }
}

export function validateGraph(nodes: StepFlowNode[]): ValidationResult {
  const errors: string[] = [];
  if (nodes.length === 0) {
    errors.push("Add at least one step.");
  }

  const names = nodes.map((n) => n.data.name.trim());
  if (names.some((n) => n === "")) {
    errors.push("Every step needs a name.");
  }
  const dupes = names.filter((n, i) => n !== "" && names.indexOf(n) !== i);
  if (dupes.length > 0) {
    errors.push(`Step names must be unique (duplicate: ${[...new Set(dupes)].join(", ")}).`);
  }

  if (nodes.some((n) => !n.data.taskDefinitionId)) {
    errors.push("Every step must reference a task.");
  }

  if (nodes.some((n) => n.data.strategy === "SpecificNode" && !n.data.targetNodeId)) {
    errors.push("Steps using a specific node must select one.");
  }
  if (
    nodes.some(
      (n) => n.data.strategy === "TaggedNodes" && Object.keys(n.data.targetTags).length === 0,
    )
  ) {
    errors.push("Steps targeting tagged nodes need at least one tag.");
  }

  if (nodes.some((n) => !parseStepParams(n.data.runtimeParamsRaw ?? "").ok)) {
    errors.push("A step's params are not a valid JSON object.");
  }

  return { ok: errors.length === 0, errors };
}

/** Maps the graph to the create-request step list. dependsOn uses step NAMES
 *  (the write contract). Order is the topological depth so the backend keeps a
 *  sensible execution order even before it recomputes its own. */
export function graphToSteps(
  nodes: StepFlowNode[],
  edges: Edge[],
): DraftPipelineStep[] {
  const incoming = new Map<string, string[]>();
  for (const e of edges) {
    const list = incoming.get(e.target) ?? [];
    list.push(e.source);
    incoming.set(e.target, list);
  }

  const nameById = new Map(nodes.map((n) => [n.id, n.data.name.trim()]));

  // Topological depth for ordering.
  const depth = new Map<string, number>();
  const computeDepth = (id: string, guard: Set<string>): number => {
    if (depth.has(id)) return depth.get(id)!;
    if (guard.has(id)) return 0;
    guard.add(id);
    const deps = incoming.get(id) ?? [];
    const d = deps.length === 0 ? 0 : 1 + Math.max(...deps.map((p) => computeDepth(p, guard)));
    guard.delete(id);
    depth.set(id, d);
    return d;
  };
  nodes.forEach((n) => computeDepth(n.id, new Set()));

  return nodes.map((n) => ({
    name: n.data.name.trim(),
    taskDefinitionId: n.data.taskDefinitionId!,
    dependsOn: (incoming.get(n.id) ?? [])
      .map((src) => nameById.get(src) ?? "")
      .filter(Boolean),
    strategy: n.data.strategy,
    targetNodeId: n.data.strategy === "SpecificNode" ? n.data.targetNodeId : null,
    targetTags:
      n.data.strategy === "TaggedNodes" && Object.keys(n.data.targetTags).length > 0
        ? n.data.targetTags
        : null,
    failurePolicy: n.data.failurePolicy,
    order: depth.get(n.id) ?? 0,
    outputMappings: (n.data.outputMappings ?? []).filter(
      (m) => m.fromStep && m.toParam,
    ),
    runtimeParams: (() => {
      const parsed = parseStepParams(n.data.runtimeParamsRaw ?? "");
      return parsed.ok ? parsed.value ?? null : null;
    })(),
  }));
}

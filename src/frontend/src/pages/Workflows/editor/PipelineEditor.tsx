import {
  addEdge,
  Background,
  MarkerType,
  ReactFlow,
  ReactFlowProvider,
  useEdgesState,
  useNodesState,
  type Connection,
  type Edge,
} from "@xyflow/react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { apiClient } from "../../../services/api";
import { queryKeys } from "../../../services/queryKeys";
import { uuid } from "../../../utils/id";
import {
  graphToSteps,
  validateGraph,
  wouldCycle,
  type StepFlowNode,
  type StepNodeData,
} from "./graphToDraft";
import { StepInspector } from "./StepInspector";
import { StepNode } from "./StepNode";
// @ts-ignore
import "@xyflow/react/dist/style.css";

const nodeTypes = { step: StepNode };

const edgeOptions = {
  type: "smoothstep" as const,
  markerEnd: { type: MarkerType.ArrowClosed },
  style: { stroke: "var(--swarm-border-strong)" },
};

let stepCounter = 0;

/** Names of all transitive ancestor steps of `selectedId`, walking edges upward.
 *  These are the only steps whose output a step may map from (P1-8). */
function ancestorNames(
  selectedId: string,
  nodes: StepFlowNode[],
  edges: Edge[],
): string[] {
  const visited = new Set<string>();
  const queue = [selectedId];
  while (queue.length) {
    const id = queue.shift()!;
    for (const e of edges) {
      if (e.target === id && !visited.has(e.source)) {
        visited.add(e.source);
        queue.push(e.source);
      }
    }
  }
  return nodes
    .filter((n) => visited.has(n.id))
    .map((n) => n.data.name.trim())
    .filter(Boolean);
}

function newStep(position: { x: number; y: number }): StepFlowNode {
  stepCounter += 1;
  return {
    id: uuid(),
    type: "step",
    position,
    data: {
      name: `step-${stepCounter}`,
      taskDefinitionId: null,
      strategy: null,
      targetNodeId: null,
      targetTags: {},
      failurePolicy: "FailPipeline",
      outputMappings: [],
      runtimeParamsRaw: "",
    } satisfies StepNodeData,
  };
}

function EditorInner() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [nodes, setNodes, onNodesChange] = useNodesState<StepFlowNode>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [cycleWarning, setCycleWarning] = useState(false);
  const addOffset = useRef(0);

  const tasksQuery = useQuery({
    queryKey: queryKeys.tasks,
    queryFn: () => apiClient.getTasks(),
  });
  const tasks = tasksQuery.data ?? [];

  const clusterNodesQuery = useQuery({
    queryKey: queryKeys.nodes,
    queryFn: () => apiClient.getNodes(),
  });
  const clusterNodes = clusterNodesQuery.data ?? [];

  const onConnect = useCallback(
    (conn: Connection) => {
      if (
        conn.source &&
        conn.target &&
        wouldCycle(edges, conn.source, conn.target)
      ) {
        setCycleWarning(true);
        window.setTimeout(() => setCycleWarning(false), 2500);
        return;
      }
      setEdges((eds) => addEdge({ ...conn, ...edgeOptions }, eds));
    },
    [edges, setEdges],
  );

  const addStep = () => {
    addOffset.current = (addOffset.current + 1) % 6;
    const node = newStep({
      x: 80 + addOffset.current * 30,
      y: 80 + addOffset.current * 60,
    });
    setNodes((nds) => [...nds, node]);
    setSelectedId(node.id);
  };

  const patchSelected = (patch: Partial<StepNodeData>) => {
    setNodes((nds) =>
      nds.map((n) =>
        n.id === selectedId ? { ...n, data: { ...n.data, ...patch } } : n,
      ),
    );
  };

  const deleteSelected = () => {
    setNodes((nds) => nds.filter((n) => n.id !== selectedId));
    setEdges((eds) =>
      eds.filter((e) => e.source !== selectedId && e.target !== selectedId),
    );
    setSelectedId(null);
  };

  const selected = nodes.find((n) => n.id === selectedId) ?? null;

  const validation = useMemo(() => {
    const base = validateGraph(nodes);
    const errors = [...base.errors];
    if (name.trim() === "") errors.push("Name the pipeline.");
    return { ok: errors.length === 0, errors };
  }, [nodes, edges, name]);

  const save = useMutation({
    mutationFn: () =>
      apiClient.createPipeline({
        name: name.trim(),
        description: description.trim() || null,
        steps: graphToSteps(nodes, edges),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.pipelines });
      navigate("/workflows");
    },
  });

  return (
    <div className="flex h-full flex-col">
      <header className="flex flex-wrap items-center gap-3 border-b border-[var(--swarm-border)] bg-[var(--swarm-chrome)] px-4 py-2.5">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Pipeline name"
          aria-label="Pipeline name"
          className="min-w-[12rem] flex-1 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
        <input
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description (optional)"
          aria-label="Pipeline description"
          className="min-w-[12rem] flex-[2] rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
        <button
          type="button"
          onClick={() => navigate("/workflows")}
          className="rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={() => save.mutate()}
          disabled={!validation.ok || save.isPending}
          title={validation.ok ? undefined : validation.errors.join("\n")}
          className="rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {save.isPending ? "Saving…" : "Save pipeline"}
        </button>
      </header>

      <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 px-6 text-center lg:hidden">
        <p className="text-sm font-medium text-[var(--swarm-ink)]">
          The pipeline canvas needs a wider screen
        </p>
        <p className="max-w-sm text-sm text-[var(--swarm-muted)]">
          Authoring relies on dragging connections between steps. Open this
          editor on a screen at least 1024px wide to build a pipeline.
        </p>
      </div>

      <div className="hidden min-h-0 flex-1 lg:flex">
        <div className="relative min-w-0 flex-1">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={(_, n) => setSelectedId(n.id)}
            onPaneClick={() => setSelectedId(null)}
            defaultEdgeOptions={edgeOptions}
            fitView
            proOptions={{ hideAttribution: true }}
          >
            <Background color="var(--swarm-border)" gap={20} />
            {/* <Controls showInteractive={false} /> */}
          </ReactFlow>

          <button
            type="button"
            onClick={addStep}
            className="absolute left-4 top-4 z-10 inline-flex items-center gap-1.5 rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] shadow-sm transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            + Add step
          </button>

          {cycleWarning && (
            <div
              role="alert"
              className="absolute left-1/2 top-4 z-10 -translate-x-1/2 rounded-md border border-[var(--swarm-warning)]/40 bg-[var(--swarm-warning-subtle)] px-3 py-1.5 text-sm text-[var(--swarm-warning)]"
            >
              That connection would create a cycle.
            </div>
          )}

          {nodes.length === 0 && (
            <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
              <p className="text-sm text-[var(--swarm-muted)]">
                Add a step to begin. Drag from a step's right edge to another's
                left to set a dependency.
              </p>
            </div>
          )}
        </div>

        <aside className="w-2/6 shrink-0 overflow-y-auto border-l border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-4">
          {selected ? (
            <StepInspector
              key={selected.id}
              data={selected.data}
              tasks={tasks}
              nodes={clusterNodes}
              ancestorNames={ancestorNames(selected.id, nodes, edges)}
              onChange={patchSelected}
              onDelete={deleteSelected}
            />
          ) : (
            <div className="text-sm text-[var(--swarm-muted)]">
              <p className="font-medium text-[var(--swarm-ink)]">
                No step selected
              </p>
              <p className="mt-1">
                Select a step to edit its task, strategy, and failure policy.
              </p>
              {!validation.ok && nodes.length > 0 && (
                <ul className="mt-4 space-y-1 text-xs text-[var(--swarm-warning)]">
                  {validation.errors.map((e) => (
                    <li key={e}>• {e}</li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {save.isError && (
            <p role="alert" className="mt-4 text-sm text-[var(--swarm-danger)]">
              Could not save the pipeline. The cluster rejected the graph or is
              unreachable.
            </p>
          )}
        </aside>
      </div>
    </div>
  );
}

export function PipelineEditor() {
  return (
    <ReactFlowProvider>
      <EditorInner />
    </ReactFlowProvider>
  );
}

import { TagMapEditor } from "../../../components/ui/TagMapEditor";
import type { DispatchStrategy, FailurePolicy, Node, TaskDefinition } from "../../../store/store";
import { FAILURE_LABEL, STRATEGY_LABEL } from "../pipelineGraph";
import type { StepNodeData } from "./graphToDraft";

const STRATEGIES: DispatchStrategy[] = [
  "AnyOnlineNode",
  "SpecificNode",
  "AllOnlineNodes",
  "TaggedNodes",
];
const POLICIES: FailurePolicy[] = ["FailPipeline", "ContinuePipeline"];

interface StepInspectorProps {
  data: StepNodeData;
  tasks: TaskDefinition[];
  nodes: Node[];
  onChange: (patch: Partial<StepNodeData>) => void;
  onDelete: () => void;
}

const fieldClass =
  "w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";
const labelClass = "mb-1 block text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]";

export function StepInspector({ data, tasks, nodes, onChange, onDelete }: StepInspectorProps) {
  return (
    <div className="flex flex-col gap-4">
      <div>
        <label htmlFor="step-name" className={labelClass}>
          Step name
        </label>
        <input
          id="step-name"
          value={data.name}
          onChange={(e) => onChange({ name: e.target.value })}
          placeholder="extract"
          className={fieldClass}
        />
      </div>

      <div>
        <label htmlFor="step-task" className={labelClass}>
          Task
        </label>
        <select
          id="step-task"
          value={data.taskDefinitionId ?? ""}
          onChange={(e) => onChange({ taskDefinitionId: e.target.value || null })}
          className={fieldClass}
        >
          <option value="" disabled>
            {tasks.length === 0 ? "No tasks defined" : "Select a task…"}
          </option>
          {tasks.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
      </div>

      <div>
        <label htmlFor="step-strategy" className={labelClass}>
          Dispatch strategy
        </label>
        <select
          id="step-strategy"
          value={data.strategy ?? ""}
          onChange={(e) =>
            onChange({ strategy: (e.target.value || null) as DispatchStrategy | null })
          }
          className={fieldClass}
        >
          <option value="">Inherit default</option>
          {STRATEGIES.map((s) => (
            <option key={s} value={s}>
              {STRATEGY_LABEL[s]}
            </option>
          ))}
        </select>
      </div>

      {data.strategy === "SpecificNode" && (
        <div>
          <label htmlFor="step-node" className={labelClass}>
            Target node
          </label>
          <select
            id="step-node"
            value={data.targetNodeId ?? ""}
            onChange={(e) => onChange({ targetNodeId: e.target.value || null })}
            className={fieldClass}
          >
            <option value="" disabled>
              {nodes.length === 0 ? "No nodes registered" : "Select a node…"}
            </option>
            {nodes.map((n) => (
              <option key={n.id} value={n.id}>
                {n.name}
              </option>
            ))}
          </select>
        </div>
      )}

      {data.strategy === "TaggedNodes" && (
        <div>
          <span className={labelClass}>Target tags</span>
          <TagMapEditor
            value={data.targetTags}
            onChange={(targetTags) => onChange({ targetTags })}
          />
        </div>
      )}

      <div>
        <label htmlFor="step-policy" className={labelClass}>
          On failure
        </label>
        <select
          id="step-policy"
          value={data.failurePolicy}
          onChange={(e) => onChange({ failurePolicy: e.target.value as FailurePolicy })}
          className={fieldClass}
        >
          {POLICIES.map((p) => (
            <option key={p} value={p}>
              {FAILURE_LABEL[p]}
            </option>
          ))}
        </select>
      </div>

      <button
        type="button"
        onClick={onDelete}
        className="mt-2 inline-flex items-center justify-center rounded-md border border-[var(--swarm-border)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-danger)] transition-colors hover:bg-[var(--swarm-danger-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        style={{ transitionDuration: "var(--swarm-duration)" }}
      >
        Remove step
      </button>
    </div>
  );
}

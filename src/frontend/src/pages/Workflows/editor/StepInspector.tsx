import { TagMapEditor } from "../../../components/ui/TagMapEditor";
import type {
  DispatchStrategy,
  FailurePolicy,
  Node,
  OutputMapping,
  TaskDefinition,
} from "../../../store/store";
import { FAILURE_LABEL, STRATEGY_LABEL } from "../pipelineGraph";
import { parseStepParams, type StepNodeData } from "./graphToDraft";

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
  /** Names of upstream steps whose output this step may map from (P1-8). */
  ancestorNames: string[];
  onChange: (patch: Partial<StepNodeData>) => void;
  onDelete: () => void;
}

const fieldClass =
  "w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";
const labelClass = "mb-1 block text-xs font-medium uppercase tracking-wide text-[var(--swarm-muted)]";

export function StepInspector({
  data,
  tasks,
  nodes,
  ancestorNames,
  onChange,
  onDelete,
}: StepInspectorProps) {
  const mappings = data.outputMappings ?? [];
  const paramsInvalid = !parseStepParams(data.runtimeParamsRaw ?? "").ok;

  const updateMapping = (index: number, patch: Partial<OutputMapping>) => {
    onChange({
      outputMappings: mappings.map((m, i) => (i === index ? { ...m, ...patch } : m)),
    });
  };
  const addMapping = () => {
    onChange({
      outputMappings: [...mappings, { fromStep: ancestorNames[0] ?? "", fromPath: "", toParam: "" }],
    });
  };
  const removeMapping = (index: number) => {
    onChange({ outputMappings: mappings.filter((_, i) => i !== index) });
  };

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

      <div>
        <label htmlFor="step-params" className={labelClass}>
          Step params <span className="font-normal lowercase">(JSON)</span>
        </label>
        <textarea
          id="step-params"
          value={data.runtimeParamsRaw}
          onChange={(e) => onChange({ runtimeParamsRaw: e.target.value })}
          rows={4}
          spellCheck={false}
          placeholder={'{ "endpoint": "/users" }'}
          aria-invalid={paramsInvalid || undefined}
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
        />
        {paramsInvalid ? (
          <p className="mt-1 text-xs text-[var(--swarm-danger)]">Must be a JSON object.</p>
        ) : (
          <p className="mt-1 text-xs text-[var(--swarm-muted)]">
            Literal params for this step. Lets two steps sharing a task be parameterized
            differently via <code className="font-mono">{"{param:key}"}</code> in the config.
          </p>
        )}
      </div>

      {(ancestorNames.length > 0 || mappings.length > 0) && (
        <div>
          <div className="mb-1 flex items-center justify-between">
            <span className={labelClass} style={{ marginBottom: 0 }}>
              Output mappings
            </span>
            {ancestorNames.length > 0 && (
              <button
                type="button"
                onClick={addMapping}
                className="rounded px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                style={{ transitionDuration: "var(--swarm-duration)" }}
              >
                + Add
              </button>
            )}
          </div>

          {ancestorNames.length === 0 && mappings.length > 0 && (
            <p className="mb-2 text-xs text-[var(--swarm-warning)]">
              These mappings reference steps that are no longer upstream. Reconnect the
              dependency or remove them.
            </p>
          )}

          {mappings.length === 0 ? (
            <p className="text-xs text-[var(--swarm-muted)]">
              Pull a value from an upstream step's result into this step's params.
            </p>
          ) : (
            <ul className="space-y-2">
              {mappings.map((m, i) => (
                <li key={i} className="flex items-center gap-1.5">
                  <select
                    value={m.fromStep}
                    onChange={(e) => updateMapping(i, { fromStep: e.target.value })}
                    aria-label="From step"
                    className="min-w-0 flex-1 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1 text-xs text-[var(--swarm-ink)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
                  >
                    {/* Keep a stale value selectable so it isn't silently dropped. */}
                    {!ancestorNames.includes(m.fromStep) && m.fromStep && (
                      <option value={m.fromStep}>{m.fromStep} (removed)</option>
                    )}
                    {ancestorNames.map((name) => (
                      <option key={name} value={name}>
                        {name}
                      </option>
                    ))}
                  </select>
                  <input
                    value={m.fromPath}
                    onChange={(e) => updateMapping(i, { fromPath: e.target.value })}
                    placeholder="rows[0].email"
                    aria-label="Result path"
                    spellCheck={false}
                    className="min-w-0 flex-1 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
                  />
                  <span className="text-[var(--swarm-muted)]" aria-hidden>
                    →
                  </span>
                  <input
                    value={m.toParam}
                    onChange={(e) => updateMapping(i, { toParam: e.target.value })}
                    placeholder="param_key"
                    aria-label="To param"
                    spellCheck={false}
                    className="min-w-0 flex-1 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-1 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
                  />
                  <button
                    type="button"
                    onClick={() => removeMapping(i)}
                    aria-label="Remove mapping"
                    className="shrink-0 rounded px-1 text-[var(--swarm-muted)] transition-colors hover:text-[var(--swarm-danger)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
                    style={{ transitionDuration: "var(--swarm-duration)" }}
                  >
                    ✕
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

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

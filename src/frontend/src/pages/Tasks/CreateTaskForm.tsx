import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { TagMapEditor } from "../../components/ui/TagMapEditor";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import type { DispatchStrategy, TaskDefinition } from "../../store/store";
import { STRATEGY_LABEL } from "../Workflows/pipelineGraph";
import {
  parseConfigWithPlaceholders,
  toSpecs,
  validateConfig,
  type TaskTypeSpec,
} from "./taskTypes";

const STRATEGIES: DispatchStrategy[] = [
  "AnyOnlineNode",
  "SpecificNode",
  "AllOnlineNodes",
  "TaggedNodes",
];

interface CreateTaskFormProps {
  onClose: () => void;
  /** When provided, the form edits this task (PUT) instead of creating. */
  task?: TaskDefinition;
}

interface ConfigCheck {
  jsonError: string | null;
  schemaErrors: string[];
}

function checkConfig(value: string, spec: TaskTypeSpec | undefined): ConfigCheck {
  if (value.trim() === "") return { jsonError: "Config is required.", schemaErrors: [] };
  const parsed = parseConfigWithPlaceholders(value);
  if (!parsed.ok) {
    return { jsonError: "Config must be valid JSON.", schemaErrors: [] };
  }
  if (typeof parsed.value !== "object" || parsed.value === null || Array.isArray(parsed.value)) {
    return { jsonError: "Config must be a JSON object.", schemaErrors: [] };
  }
  // Type-check only when the config is strict JSON. With unquoted value-position
  // placeholders (lenient parse), the surrogate substitution would mis-type
  // fields, so defer type validation to the cluster's placeholder-aware check.
  const schemaErrors =
    !parsed.lenient && spec
      ? validateConfig(parsed.value as Record<string, unknown>, spec.schema)
      : [];
  return { jsonError: null, schemaErrors };
}

const inputClass =
  "w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";
const fieldLabel = "mb-1 block text-sm font-medium text-[var(--swarm-ink)]";

export function CreateTaskForm({ onClose, task }: CreateTaskFormProps) {
  const queryClient = useQueryClient();
  const editing = Boolean(task);
  const [name, setName] = useState(task?.name ?? "");
  const [description, setDescription] = useState(task?.description ?? "");
  const [taskType, setTaskType] = useState(task?.taskType ?? "");
  const [configJson, setConfigJson] = useState(task?.configJson ?? "");
  const [strategy, setStrategy] = useState<DispatchStrategy>(
    task?.defaultStrategy ?? "AnyOnlineNode",
  );
  const [targetTags, setTargetTags] = useState<Record<string, string>>(() => {
    if (!task?.defaultTargetTagsJson) return {};
    try {
      return JSON.parse(task.defaultTargetTagsJson) ?? {};
    } catch {
      return {};
    }
  });
  const [touched, setTouched] = useState(false);

  const catalogQuery = useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: () => apiClient.getCapabilities(),
  });
  const specs = catalogQuery.data ? toSpecs(catalogQuery.data) : [];
  const spec = specs.find((s) => s.id === taskType);

  // Default to the first advertised task type once the catalog loads (create only).
  useEffect(() => {
    if (!taskType && specs.length > 0) setTaskType(specs[0].id);
  }, [specs, taskType]);

  const defaultTargetTags = strategy === "TaggedNodes" ? targetTags : null;

  const save = useMutation({
    mutationFn: () =>
      editing
        ? apiClient.updateTask(task!.id, {
            name: name.trim(),
            description: description.trim(),
            taskType,
            configJson,
            defaultStrategy: strategy,
            defaultTargetTags,
            expectedVersion: task!.version,
          })
        : apiClient.createTask({
            name: name.trim(),
            description: description.trim(),
            taskType,
            configJson,
            defaultStrategy: strategy,
            defaultTargetTags,
          }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.tasks });
      if (editing) {
        queryClient.invalidateQueries({ queryKey: queryKeys.taskVersions(task!.id) });
      }
      onClose();
    },
  });
  const create = save; // keep existing references below working

  const { jsonError, schemaErrors } = checkConfig(configJson, spec);
  const nameError = name.trim() === "" ? "Name is required." : null;
  const noTaskTypes = catalogQuery.isSuccess && specs.length === 0;
  const typeError = taskType === "" && !catalogQuery.isLoading ? "Select a task type." : null;
  const canSubmit =
    !jsonError &&
    schemaErrors.length === 0 &&
    !nameError &&
    !typeError &&
    !create.isPending;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        setTouched(true);
        if (canSubmit) create.mutate();
      }}
      className="space-y-4 rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-4"
    >
      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <label htmlFor="task-name" className={fieldLabel}>
            Name
          </label>
          <input
            id="task-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={() => setTouched(true)}
            placeholder="http-health-check"
            className={inputClass}
          />
          {touched && nameError && (
            <p className="mt-1 text-xs text-[var(--swarm-danger)]">{nameError}</p>
          )}
        </div>
        <div>
          <label htmlFor="task-desc" className={fieldLabel}>
            Description <span className="font-normal text-[var(--swarm-muted)]">(optional)</span>
          </label>
          <input
            id="task-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Pings the service health endpoint"
            className={inputClass}
          />
        </div>
        <div>
          <label htmlFor="task-type" className={fieldLabel}>
            Task type
          </label>
          <select
            id="task-type"
            value={taskType}
            onChange={(e) => setTaskType(e.target.value)}
            disabled={catalogQuery.isLoading || noTaskTypes}
            className={inputClass}
          >
            {catalogQuery.isLoading && <option value="">Loading…</option>}
            {noTaskTypes && <option value="">No handlers advertised</option>}
            {specs.map((t) => (
              <option key={t.id} value={t.id}>
                {t.label}
              </option>
            ))}
          </select>
          {noTaskTypes && (
            <p className="mt-1 text-xs text-[var(--swarm-muted)]">
              No online node advertises a handler. Start a node to author tasks.
            </p>
          )}
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="task-strategy" className={fieldLabel}>
            Default dispatch strategy
          </label>
          <select
            id="task-strategy"
            value={strategy}
            onChange={(e) => setStrategy(e.target.value as DispatchStrategy)}
            className={inputClass}
          >
            {STRATEGIES.map((s) => (
              <option key={s} value={s}>
                {STRATEGY_LABEL[s]}
              </option>
            ))}
          </select>
          <p className="mt-1 text-xs text-[var(--swarm-muted)]">
            Used when dispatching this task unless overridden at dispatch or per pipeline step.
          </p>
        </div>
        {strategy === "TaggedNodes" && (
          <div>
            <span className={fieldLabel}>Default target tags</span>
            <TagMapEditor value={targetTags} onChange={setTargetTags} />
          </div>
        )}
      </div>

      <div>
        <div className="mb-1 flex items-center justify-between">
          <label htmlFor="task-config" className={fieldLabel} style={{ marginBottom: 0 }}>
            Config (JSON)
          </label>
          {spec && spec.example !== "{}" && (
            <button
              type="button"
              onClick={() => setConfigJson(spec.example)}
              className="rounded px-1.5 py-0.5 text-xs font-medium text-[var(--swarm-primary)] transition-colors hover:bg-[var(--swarm-primary-subtle)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
              style={{ transitionDuration: "var(--swarm-duration)" }}
            >
              Insert example
            </button>
          )}
        </div>
        <textarea
          id="task-config"
          value={configJson}
          onChange={(e) => setConfigJson(e.target.value)}
          onBlur={() => setTouched(true)}
          rows={9}
          spellCheck={false}
          placeholder={spec?.example}
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
          aria-invalid={touched && (jsonError || schemaErrors.length > 0) ? true : undefined}
        />
        {touched && jsonError && (
          <p className="mt-1 text-xs text-[var(--swarm-danger)]">{jsonError}</p>
        )}
        {touched && !jsonError && schemaErrors.length > 0 && (
          <ul className="mt-1 space-y-0.5">
            {schemaErrors.map((err) => (
              <li key={err} className="text-xs text-[var(--swarm-danger)]">
                {err}
              </li>
            ))}
          </ul>
        )}
        {spec && (
          <div className="mt-1 space-y-0.5 text-xs text-[var(--swarm-muted)]">
            <p>
              {spec.schema
                ? `Validated against the ${spec.id} handler schema. `
                : `The ${spec.id} handler reports no schema; only JSON validity is checked. `}
              Placeholders like <code className="font-mono">{"{env:KEY}"}</code> are accepted.
            </p>
            {spec.requiredEnvKeys.length > 0 && (
              <p>
                Requires env keys:{" "}
                <span className="font-mono">{spec.requiredEnvKeys.join(", ")}</span>
              </p>
            )}
            {spec.requiredParams.length > 0 && (
              <p>
                Requires runtime params:{" "}
                <span className="font-mono">{spec.requiredParams.join(", ")}</span>
              </p>
            )}
          </div>
        )}
      </div>

      {create.isError && (
        <p role="alert" className="text-sm text-[var(--swarm-danger)]">
          Could not create the task. The cluster rejected the request.
        </p>
      )}

      <div className="flex items-center gap-2">
        <button
          type="submit"
          disabled={!canSubmit}
          className="inline-flex items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          {create.isPending
            ? editing
              ? "Saving…"
              : "Creating…"
            : editing
              ? "Save changes"
              : "Create task"}
        </button>
        <button
          type="button"
          onClick={onClose}
          className="inline-flex items-center rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}

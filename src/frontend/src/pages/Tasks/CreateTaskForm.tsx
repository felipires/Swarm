import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";
import { toSpecs, validateConfig, type TaskTypeSpec } from "./taskTypes";

interface CreateTaskFormProps {
  onClose: () => void;
}

interface ConfigCheck {
  jsonError: string | null;
  schemaErrors: string[];
}

function checkConfig(value: string, spec: TaskTypeSpec | undefined): ConfigCheck {
  if (value.trim() === "") return { jsonError: "Config is required.", schemaErrors: [] };
  let parsed: unknown;
  try {
    parsed = JSON.parse(value);
  } catch {
    return { jsonError: "Config must be valid JSON.", schemaErrors: [] };
  }
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) {
    return { jsonError: "Config must be a JSON object.", schemaErrors: [] };
  }
  const schemaErrors = spec
    ? validateConfig(parsed as Record<string, unknown>, spec.schema)
    : [];
  return { jsonError: null, schemaErrors };
}

const inputClass =
  "w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25";
const fieldLabel = "mb-1 block text-sm font-medium text-[var(--swarm-ink)]";

export function CreateTaskForm({ onClose }: CreateTaskFormProps) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [taskType, setTaskType] = useState("");
  const [configJson, setConfigJson] = useState("");
  const [touched, setTouched] = useState(false);

  const catalogQuery = useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: () => apiClient.getCapabilities(),
  });
  const specs = catalogQuery.data ? toSpecs(catalogQuery.data) : [];
  const spec = specs.find((s) => s.id === taskType);

  // Default to the first advertised task type once the catalog loads.
  useEffect(() => {
    if (!taskType && specs.length > 0) setTaskType(specs[0].id);
  }, [specs, taskType]);

  const create = useMutation({
    mutationFn: () =>
      apiClient.createTask({
        name: name.trim(),
        description: description.trim(),
        taskType,
        configJson,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.tasks });
      onClose();
    },
  });

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
          {create.isPending ? "Creating…" : "Create task"}
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

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiClient } from "../../services/api";
import { queryKeys } from "../../services/queryKeys";

interface CreateTaskFormProps {
  onClose: () => void;
}

function validateJson(value: string): string | null {
  if (value.trim() === "") return "Config is required.";
  try {
    JSON.parse(value);
    return null;
  } catch {
    return "Config must be valid JSON.";
  }
}

const PLACEHOLDER = `{
  "type": "Http",
  "url": "https://example.com/health",
  "method": "GET"
}`;

export function CreateTaskForm({ onClose }: CreateTaskFormProps) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [configJson, setConfigJson] = useState("");
  const [touched, setTouched] = useState(false);

  const create = useMutation({
    mutationFn: () => apiClient.createTask(name.trim(), description.trim(), configJson),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.tasks });
      onClose();
    },
  });

  const jsonError = validateJson(configJson);
  const nameError = name.trim() === "" ? "Name is required." : null;
  const canSubmit = !jsonError && !nameError && !create.isPending;

  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        setTouched(true);
        if (canSubmit) create.mutate();
      }}
      className="space-y-4 rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-4"
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label htmlFor="task-name" className="mb-1 block text-sm font-medium text-[var(--swarm-ink)]">
            Name
          </label>
          <input
            id="task-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            onBlur={() => setTouched(true)}
            placeholder="http-health-check"
            className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
          />
          {touched && nameError && (
            <p className="mt-1 text-xs text-[var(--swarm-danger)]">{nameError}</p>
          )}
        </div>
        <div>
          <label htmlFor="task-desc" className="mb-1 block text-sm font-medium text-[var(--swarm-ink)]">
            Description <span className="font-normal text-[var(--swarm-muted)]">(optional)</span>
          </label>
          <input
            id="task-desc"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Pings the service health endpoint"
            className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
          />
        </div>
      </div>

      <div>
        <label htmlFor="task-config" className="mb-1 block text-sm font-medium text-[var(--swarm-ink)]">
          Config (JSON)
        </label>
        <textarea
          id="task-config"
          value={configJson}
          onChange={(e) => setConfigJson(e.target.value)}
          onBlur={() => setTouched(true)}
          rows={7}
          spellCheck={false}
          placeholder={PLACEHOLDER}
          className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-xs text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
          aria-invalid={touched && jsonError ? true : undefined}
        />
        {touched && jsonError && (
          <p className="mt-1 text-xs text-[var(--swarm-danger)]">{jsonError}</p>
        )}
      </div>

      {create.isError && (
        <p role="alert" className="text-sm text-[var(--swarm-danger)]">
          Could not create the task. Check the config and try again.
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

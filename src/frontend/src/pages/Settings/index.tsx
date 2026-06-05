import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { apiClient } from "../../services/api";
import {
  DEFAULT_BASE_URL,
  loadSettings,
  saveSettings,
  type ApiSettings,
} from "../../services/settings";

type TestState =
  | { status: "idle" }
  | { status: "testing" }
  | { status: "ok" }
  | { status: "fail"; reason: string };

export const SettingsPage = () => {
  const queryClient = useQueryClient();
  const [saved, setSaved] = useState<ApiSettings>(() => loadSettings());
  const [baseUrl, setBaseUrl] = useState(saved.baseUrl);
  const [apiKey, setApiKey] = useState(saved.apiKey);
  const [showKey, setShowKey] = useState(false);
  const [test, setTest] = useState<TestState>({ status: "idle" });
  const [savedFlash, setSavedFlash] = useState(false);

  const draft: ApiSettings = { baseUrl: baseUrl.trim(), apiKey: apiKey.trim() };
  const dirty = draft.baseUrl !== saved.baseUrl || draft.apiKey !== saved.apiKey;

  const handleSave = () => {
    saveSettings(draft);
    apiClient.applySettings(draft);
    setSaved(draft);
    setSavedFlash(true);
    window.setTimeout(() => setSavedFlash(false), 2000);
    // Refetch everything against the new endpoint/credentials.
    queryClient.invalidateQueries();
  };

  const handleReset = () => {
    setBaseUrl(DEFAULT_BASE_URL);
    setApiKey("");
  };

  const handleTest = async () => {
    setTest({ status: "testing" });
    try {
      const ok = await apiClient.checkHealth(draft);
      setTest(ok ? { status: "ok" } : { status: "fail", reason: "Unexpected response." });
    } catch {
      setTest({ status: "fail", reason: "Could not reach the cluster." });
    }
  };

  return (
    <div className="mx-auto flex max-w-2xl flex-col gap-6 px-6 py-6">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]">
          Settings
        </h1>
        <p className="mt-1 text-sm text-[var(--swarm-muted)]">
          Cluster connection and credentials. Stored locally in this browser.
        </p>
      </header>

      <section className="space-y-5 rounded-lg border border-[var(--swarm-border)] bg-[var(--swarm-surface)] p-5">
        <div>
          <label htmlFor="base-url" className="mb-1 block text-sm font-medium text-[var(--swarm-ink)]">
            API base URL
          </label>
          <input
            id="base-url"
            type="url"
            value={baseUrl}
            onChange={(e) => setBaseUrl(e.target.value)}
            placeholder={DEFAULT_BASE_URL}
            spellCheck={false}
            className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 font-mono text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
          />
          <p className="mt-1 text-xs text-[var(--swarm-muted)]">
            Default: <span className="font-mono">{DEFAULT_BASE_URL}</span>
          </p>
        </div>

        <div>
          <label htmlFor="api-key" className="mb-1 block text-sm font-medium text-[var(--swarm-ink)]">
            API key <span className="font-normal text-[var(--swarm-muted)]">(optional)</span>
          </label>
          <div className="flex gap-2">
            <input
              id="api-key"
              type={showKey ? "text" : "password"}
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              placeholder="Sent as the X-API-Key header"
              autoComplete="off"
              spellCheck={false}
              className="w-full rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-1.5 font-mono text-sm text-[var(--swarm-ink)] placeholder:text-[var(--swarm-placeholder)] focus:border-[var(--swarm-primary)] focus:outline-none focus:ring-2 focus:ring-[var(--swarm-primary)]/25"
            />
            <button
              type="button"
              onClick={() => setShowKey((s) => !s)}
              aria-pressed={showKey}
              className="shrink-0 rounded-md border border-[var(--swarm-border)] px-3 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
              style={{ transitionDuration: "var(--swarm-duration)" }}
            >
              {showKey ? "Hide" : "Show"}
            </button>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t border-[var(--swarm-border)] pt-4">
          <button
            type="button"
            onClick={handleSave}
            disabled={!dirty}
            className="inline-flex items-center rounded-md bg-[var(--swarm-primary)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            Save changes
          </button>
          <button
            type="button"
            onClick={handleTest}
            disabled={test.status === "testing"}
            className="inline-flex items-center rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-surface)] px-3 py-1.5 text-sm font-medium text-[var(--swarm-ink)] transition-colors hover:bg-[var(--swarm-surface-raised)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)] disabled:opacity-60"
            style={{ transitionDuration: "var(--swarm-duration)" }}
          >
            {test.status === "testing" ? "Testing…" : "Test connection"}
          </button>
          {draft.baseUrl !== DEFAULT_BASE_URL && (
            <button
              type="button"
              onClick={handleReset}
              className="inline-flex items-center rounded-md px-3 py-1.5 text-sm font-medium text-[var(--swarm-muted)] transition-colors hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
              style={{ transitionDuration: "var(--swarm-duration)" }}
            >
              Reset to default
            </button>
          )}

          <span className="ml-auto" role="status" aria-live="polite">
            {savedFlash && <span className="text-sm text-[var(--swarm-success)]">Saved</span>}
            {test.status === "ok" && (
              <span className="text-sm text-[var(--swarm-success)]">Connection OK</span>
            )}
            {test.status === "fail" && (
              <span className="text-sm text-[var(--swarm-danger)]">{test.reason}</span>
            )}
          </span>
        </div>
      </section>
    </div>
  );
};

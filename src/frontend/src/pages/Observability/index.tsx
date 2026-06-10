import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { LogResults } from "./LogResults";
import { addFacet, parseLogQuery } from "./logQuery";

// Time-range presets (minutes back from now). "0" = no lower bound.
const RANGES = [
  { label: "Last 15m", minutes: 15 },
  { label: "Last 1h", minutes: 60 },
  { label: "Last 24h", minutes: 60 * 24 },
  { label: "All time", minutes: 0 },
] as const;

const REFRESH = [
  { label: "Off", ms: 0 },
  { label: "5s", ms: 5_000 },
  { label: "15s", ms: 15_000 },
] as const;

export const ObservabilityPage = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  const submitted = searchParams.get("q") ?? "";
  const rangeMin = Number(searchParams.get("range") ?? "60");
  const refreshMs = Number(searchParams.get("refresh") ?? "0");

  // Input tracks the live query bar text; initialised from URL on mount.
  // Also syncs when the URL changes externally (e.g. alert badge navigation).
  const [input, setInput] = useState(submitted);
  useEffect(() => { setInput(submitted); }, [submitted]);

  const setParam = (key: string, value: string, defaultValue: string) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (value === defaultValue) next.delete(key);
      else next.set(key, value);
      return next;
    }, { replace: true });
  };

  const params = useMemo(() => {
    const parsed = parseLogQuery(submitted);
    if (rangeMin > 0) {
      parsed.from = new Date(Date.now() - rangeMin * 60_000).toISOString();
    }
    return parsed;
  }, [submitted, rangeMin]);

  const onAddFacet = (key: string, value: string) => {
    const next = addFacet(input, key, value);
    setInput(next);
    setParam("q", next, "");
  };

  return (
    <div className="flex h-full flex-col px-6 py-6">
      <header className="mb-4">
        <h1 className="text-2xl font-semibold tracking-tight text-[var(--swarm-ink)]">Logs</h1>
        <p className="mt-1 text-sm text-[var(--swarm-muted)]">
          Search all logs by free text and tags. Try{" "}
          <code className="rounded bg-[var(--swarm-surface-raised)] px-1 text-xs">
            run:&lt;id&gt; level:error "timeout"
          </code>
          .
        </p>
      </header>

      <form
        className="mb-3 flex flex-wrap items-center gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          setParam("q", input, "");
        }}
      >
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Search logs…  task:  run:  step:  pipeline:  level:  env.KEY:"
          spellCheck={false}
          autoComplete="off"
          className="min-w-0 flex-1 rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-3 py-2 font-mono text-sm text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
        />
        <Select
          label="Range"
          value={String(rangeMin)}
          onChange={(v) => setParam("range", v, "60")}
          options={RANGES.map((r) => ({ label: r.label, value: String(r.minutes) }))}
        />
        <Select
          label="Auto-refresh"
          value={String(refreshMs)}
          onChange={(v) => setParam("refresh", v, "0")}
          options={REFRESH.map((r) => ({ label: r.label, value: String(r.ms) }))}
        />
        <button
          type="submit"
          className="rounded-md bg-[var(--swarm-primary)] px-3 py-2 text-sm font-medium text-[var(--swarm-on-primary)] transition-colors hover:bg-[var(--swarm-primary-hover)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
          style={{ transitionDuration: "var(--swarm-duration)" }}
        >
          Search
        </button>
      </form>

      <section className="min-h-0 flex-1 overflow-hidden rounded-lg border border-[var(--swarm-border)]">
        <LogResults
          params={params}
          refetchMs={refreshMs}
          onAddFacet={onAddFacet}
          emptyHint="No logs match. Adjust the query, tags, or time range."
        />
      </section>
    </div>
  );
};

function Select({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: { label: string; value: string }[];
}) {
  return (
    <label className="flex items-center gap-1.5 text-xs text-[var(--swarm-muted)]">
      <span className="hidden sm:inline">{label}</span>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="rounded-md border border-[var(--swarm-border)] bg-[var(--swarm-bg)] px-2 py-2 text-sm text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--swarm-focus)]"
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  );
}

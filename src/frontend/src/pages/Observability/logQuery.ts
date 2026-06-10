import type { LogQueryParams } from "../../store/store";

/**
 * Datadog-style log query bar parsing. The bar mixes inline `key:value` facets
 * with free text:
 *
 *   run:abc level:error env.DB:prod "connection reset"
 *
 * - `node:<id>`  → the NodeId column filter
 * - `level:<x>`  → severity threshold (that level and above)
 * - any other `key:value` → a jsonb tag facet (task, run, step, pipeline,
 *   taskDef, taskType, env.<KEY>, …), AND-combined server-side
 * - everything else → free text (substring over message + template)
 */

// Stored level names (cluster CompactLevelToFull), low→high severity.
const SEVERITY = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];
const LEVEL_ALIAS: Record<string, string> = {
  trace: "Verbose",
  verbose: "Verbose",
  debug: "Debug",
  info: "Information",
  information: "Information",
  warn: "Warning",
  warning: "Warning",
  error: "Error",
  fatal: "Fatal",
  critical: "Fatal",
};

/** Concrete level names at or above the given level (severity threshold). */
export function levelsAtOrAbove(level: string): string[] {
  const canon = LEVEL_ALIAS[level.toLowerCase()];
  if (!canon) return [];
  return SEVERITY.slice(SEVERITY.indexOf(canon));
}

/**
 * Resolve a level token from the query bar:
 *   `Debug`    → exact match
 *   `>Warning` → Warning and above
 *   `<Error`   → Error and below
 */
function resolveLevel(token: string): string[] {
  if (token.startsWith(">")) {
    const canon = LEVEL_ALIAS[token.slice(1).toLowerCase()];
    if (!canon) return [];
    return SEVERITY.slice(SEVERITY.indexOf(canon));
  }
  if (token.startsWith("<")) {
    const canon = LEVEL_ALIAS[token.slice(1).toLowerCase()];
    if (!canon) return [];
    return SEVERITY.slice(0, SEVERITY.indexOf(canon) + 1);
  }
  const canon = LEVEL_ALIAS[token.toLowerCase()];
  return canon ? [canon] : [];
}

// A facet token: key is letters/digits/dot/underscore, then ':', then a value
// (quoted or unquoted, no spaces unless quoted).
const FACET = /^([\w.]+):(.+)$/;

/** Split respecting double quotes so `"foo bar"` stays one token. */
function tokenize(input: string): string[] {
  return input.match(/"[^"]*"|\S+/g) ?? [];
}

function unquote(value: string): string {
  return value.startsWith('"') && value.endsWith('"') ? value.slice(1, -1) : value;
}

export function parseLogQuery(input: string): LogQueryParams {
  const tags: string[] = [];
  const freeText: string[] = [];
  let nodeId: string | undefined;
  let level: string[] | undefined;

  for (const token of tokenize(input)) {
    const m = token.match(FACET);
    if (!m) {
      freeText.push(unquote(token));
      continue;
    }
    const key = m[1];
    const value = unquote(m[2]);
    if (key === "node") {
      nodeId = value;
    } else if (key === "level" || key === "status") {
      const resolved = resolveLevel(value);
      if (resolved.length) {
        level = level ? [...new Set([...level, ...resolved])] : resolved;
      }
    } else {
      // `instance:` is a friendlier alias for the `task` (TaskInstanceId) tag.
      const tagKey = key === "instance" ? "task" : key;
      tags.push(`${tagKey}:${value}`);
    }
  }

  const q = freeText.join(" ").trim();
  return {
    ...(tags.length ? { tags } : {}),
    ...(level && level.length ? { level } : {}),
    ...(nodeId ? { nodeId } : {}),
    ...(q ? { q } : {}),
  };
}

/** Append a `key:value` facet to a query string (used by clickable tag chips). */
export function addFacet(input: string, key: string, value: string): string {
  const facet = `${key}:${/\s/.test(value) ? `"${value}"` : value}`;
  if (tokenize(input).includes(facet)) return input; // already present
  return input.trim() ? `${input.trim()} ${facet}` : facet;
}

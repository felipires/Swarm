// `{src:key:modifiers}` placeholder token. A real JSON object opens with `{"`
// or `{ `, never `{word:`, so these patterns never collide with object syntax.
const TOKEN = /\{[a-zA-Z_][a-zA-Z0-9_]*:[^{}]*\}/g;
// Only tokens NOT already wrapped in quotes (value-position / unquoted).
const UNQUOTED_TOKEN = /(?<!")(\{[a-zA-Z_][a-zA-Z0-9_]*:[^{}]*\})(?!")/g;

/**
 * Validity check for task **config** (sent to the cluster as a raw JSON string,
 * so unquoted value-position placeholders are allowed). Tries strict parse;
 * on failure substitutes tokens with a literal and re-parses. `lenient` is true
 * when substitution was needed — the caller submits the original text unchanged.
 */
export function parseConfigWithPlaceholders(
  raw: string,
): { ok: true; value: unknown; lenient: boolean } | { ok: false } {
  try {
    return { ok: true, value: JSON.parse(raw), lenient: false };
  } catch {
    /* fall through */
  }
  try {
    return { ok: true, value: JSON.parse(raw.replace(TOKEN, "1")), lenient: true };
  } catch {
    return { ok: false };
  }
}

/**
 * Parse **params** input (runtime params for dispatch / pipeline run / step),
 * which is sent inside the JSON request body and must therefore be valid JSON.
 * Unquoted placeholders can't round-trip as-is, so we quote them — preserving
 * the placeholder as a resolvable string the cluster substitutes at dispatch.
 * Empty input is valid (no params).
 */
export function parseParamsWithPlaceholders(
  raw: string,
): { ok: true; value?: Record<string, unknown> } | { ok: false } {
  if (raw.trim() === "") return { ok: true, value: undefined };

  const asObject = (v: unknown) =>
    typeof v === "object" && v !== null && !Array.isArray(v)
      ? ({ ok: true, value: v as Record<string, unknown> } as const)
      : ({ ok: false } as const);

  try {
    return asObject(JSON.parse(raw));
  } catch {
    /* try quoting unquoted placeholders */
  }
  try {
    return asObject(JSON.parse(raw.replace(UNQUOTED_TOKEN, '"$1"')));
  } catch {
    return { ok: false };
  }
}

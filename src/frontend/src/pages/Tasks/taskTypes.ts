import type { CapabilityCatalogEntry, JsonSchema } from "../../store/store";

/** Parsed, validated view of a capability entry the task form can author against. */
export interface TaskTypeSpec {
  id: string;
  label: string;
  schema: JsonSchema | null;
  example: string;
  requiredEnvKeys: string[];
  requiredParams: string[];
  nodeCount: number;
}

function parseSchema(raw: string): JsonSchema | null {
  try {
    const parsed = JSON.parse(raw);
    return typeof parsed === "object" && parsed !== null ? (parsed as JsonSchema) : null;
  } catch {
    return null;
  }
}

const PLACEHOLDER_RE = /\{[a-z]+:[^}]+\}/;

function isPlaceholder(v: unknown): boolean {
  return typeof v === "string" && PLACEHOLDER_RE.test(v);
}

function jsonType(v: unknown): string {
  if (v === null) return "null";
  if (Array.isArray(v)) return "array";
  return typeof v;
}

/** Synthesizes a config example from the schema: required fields first, then a
 *  few optional ones, each seeded with a type-appropriate placeholder so the
 *  user sees the shape and the resolution syntax. */
function buildExample(schema: JsonSchema | null): string {
  if (!schema?.properties) return "{}";
  const required = new Set(schema.required ?? []);
  const props = Object.entries(schema.properties);
  // Required props always; otherwise cap the example at 5 fields to stay readable.
  const chosen = props
    .filter(([k]) => required.has(k))
    .concat(props.filter(([k]) => !required.has(k)).slice(0, 5));

  const obj: Record<string, unknown> = {};
  for (const [key, prop] of chosen) {
    obj[key] = sampleFor(key, prop, required.has(key));
  }
  return JSON.stringify(obj, null, 2);
}

function sampleFor(key: string, prop: JsonSchema, isRequired: boolean): unknown {
  switch (prop.type) {
    case "integer":
    case "number":
      return isRequired ? `{param:${key}:type=int}` : 0;
    case "boolean":
      return false;
    case "array":
      return [];
    case "object":
      return {};
    default:
      return isRequired ? `{param:${key}:required}` : `{param:${key}}`;
  }
}

export function toSpecs(catalog: CapabilityCatalogEntry[]): TaskTypeSpec[] {
  return catalog.map((c) => {
    const schema = parseSchema(c.jsonSchema);
    return {
      id: c.taskType,
      label:
        c.nodeCount > 0
          ? `${c.taskType} (${c.nodeCount} node${c.nodeCount === 1 ? "" : "s"})`
          : c.taskType,
      schema,
      example: buildExample(schema),
      requiredEnvKeys: c.requiredEnvKeys,
      requiredParams: c.requiredParams,
      nodeCount: c.nodeCount,
    };
  });
}

/** Validates a parsed config object against the handler's JSON schema. Honors
 *  `required` and top-level property `type`s. A field whose value is a
 *  `{src:key}` placeholder is accepted for any declared type, mirroring the
 *  Cluster's placeholder-aware validation (D4). Returns human-readable errors. */
export function validateConfig(
  config: Record<string, unknown>,
  schema: JsonSchema | null,
): string[] {
  if (!schema) return [];
  const errors: string[] = [];

  for (const key of schema.required ?? []) {
    const value = config[key];
    if (!(key in config) || value === null || value === "") {
      errors.push(`Missing required field "${key}".`);
    }
  }

  const props = schema.properties ?? {};
  for (const [key, value] of Object.entries(config)) {
    const prop = props[key];
    if (!prop?.type || value === null) continue;
    if (isPlaceholder(value)) continue;
    const expected = prop.type === "integer" ? "number" : prop.type;
    const actual = jsonType(value);
    if (actual !== expected) {
      errors.push(`Field "${key}" should be a ${prop.type}.`);
    }
  }

  return errors;
}

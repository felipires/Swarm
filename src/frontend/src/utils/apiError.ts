/**
 * Pulls a human-readable message out of a failed request. The cluster returns a
 * structured `ApiError { code, message, details }` on every handled failure;
 * validation 400s use ProblemDetails `{ title, errors }`. Falls back to the
 * Error message, then a generic string.
 */
export function getApiErrorMessage(error: unknown, fallback = "Something went wrong."): string {
  const e = error as { response?: { data?: unknown }; message?: string } | undefined;
  const data = e?.response?.data;

  if (typeof data === "string" && data.trim()) return data;
  if (data && typeof data === "object") {
    const d = data as Record<string, unknown>;
    if (typeof d.message === "string" && d.message) return d.message;
    if (typeof d.error === "string" && d.error) return d.error; // some validation envelopes
    if (typeof d.title === "string" && d.title) return d.title; // ProblemDetails
  }
  if (e?.message) return e.message;
  return fallback;
}

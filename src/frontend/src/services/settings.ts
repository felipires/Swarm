const KEYS = {
  baseUrl: "swarm.apiBaseUrl",
  apiKey: "swarm.apiKey",
} as const;

export const DEFAULT_BASE_URL =
  (import.meta as any).env.VITE_API_URL || "http://localhost:5001/api";

function read(key: string): string {
  try {
    return localStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

function write(key: string, value: string): void {
  try {
    if (value) localStorage.setItem(key, value);
    else localStorage.removeItem(key);
  } catch {
    // localStorage unavailable (private mode / disabled) — settings won't persist.
  }
}

export interface ApiSettings {
  baseUrl: string;
  apiKey: string;
}

export function loadSettings(): ApiSettings {
  return {
    baseUrl: read(KEYS.baseUrl) || DEFAULT_BASE_URL,
    apiKey: read(KEYS.apiKey),
  };
}

export function saveSettings(settings: ApiSettings): void {
  // Persist baseUrl only when it differs from the default, so resetting to
  // default clears the override rather than pinning a now-stale value.
  write(KEYS.baseUrl, settings.baseUrl === DEFAULT_BASE_URL ? "" : settings.baseUrl);
  write(KEYS.apiKey, settings.apiKey);
}

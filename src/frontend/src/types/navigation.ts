export type NavRoute = "overview" | "workflows" | "tasks" | "observability" | "settings";

export interface NavItem {
  id: NavRoute;
  label: string;
  description: string;
  path: string;
}

export function routePath(id: NavRoute): string {
  return `/${id}`;
}

export const PRIMARY_NAV: NavItem[] = [
  {
    id: "overview",
    label: "Overview",
    description: "Cluster posture, active runs, and recent activity.",
    path: "/overview",
  },
  {
    id: "workflows",
    label: "Workflows",
    description: "Pipeline definitions, schedules, and the workflow canvas.",
    path: "/workflows",
  },
  {
    id: "tasks",
    label: "Tasks",
    description: "Task definitions, dispatch, and execution history.",
    path: "/tasks",
  },
  {
    id: "observability",
    label: "Observability",
    description: "Logs, metrics, and execution history across nodes.",
    path: "/observability",
  },
];

export const SETTINGS_NAV: NavItem = {
  id: "settings",
  label: "Settings",
  description: "Cluster connection, credentials, and notification rules.",
  path: "/settings",
};

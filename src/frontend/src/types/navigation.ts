export type NavRoute = "overview" | "workflows" | "observability" | "settings";

export interface NavItem {
  id: NavRoute;
  label: string;
  description: string;
}

export const PRIMARY_NAV: NavItem[] = [
  {
    id: "overview",
    label: "Overview",
    description: "Cluster posture, active runs, and recent activity.",
  },
  {
    id: "workflows",
    label: "Workflows",
    description: "Pipeline definitions, schedules, and the workflow canvas.",
  },
  {
    id: "observability",
    label: "Observability",
    description: "Logs, metrics, and execution history across nodes.",
  },
];

export const SETTINGS_NAV: NavItem = {
  id: "settings",
  label: "Settings",
  description: "Cluster connection, credentials, and notification rules.",
};

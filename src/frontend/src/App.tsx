import { lazy, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "./components/shell/AppShell";
import { RouteView } from "./components/shell/RouteView";
import { ObservabilityPage } from "./pages/Observability";
import { OverviewPage } from "./pages/Overview";
import { SettingsPage } from "./pages/Settings";
import { TasksPage } from "./pages/Tasks";
import { WorkflowsPage } from "./pages/Workflows";

// The canvas surfaces (React Flow) are heavy and route-specific; split them out.
const PipelineEditor = lazy(() =>
  import("./pages/Workflows/editor/PipelineEditor").then((m) => ({ default: m.PipelineEditor })),
);
const PipelineView = lazy(() =>
  import("./pages/Workflows/view/PipelineView").then((m) => ({ default: m.PipelineView })),
);

const CanvasFallback = () => (
  <div className="flex h-full items-center justify-center text-sm text-[var(--swarm-muted)]">
    Loading…
  </div>
);

function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<Navigate to="/overview" replace />} />
        <Route path="overview" element={<RouteView Page={OverviewPage} />} />
        <Route path="workflows" element={<RouteView Page={WorkflowsPage} />} />
        <Route
          path="workflows/new"
          element={
            <Suspense fallback={<CanvasFallback />}>
              <PipelineEditor />
            </Suspense>
          }
        />
        <Route
          path="workflows/:id"
          element={
            <Suspense fallback={<CanvasFallback />}>
              <PipelineView />
            </Suspense>
          }
        />
        <Route path="tasks" element={<RouteView Page={TasksPage} />} />
        <Route path="observability" element={<RouteView Page={ObservabilityPage} />} />
        <Route path="settings" element={<RouteView Page={SettingsPage} />} />
        <Route path="*" element={<Navigate to="/overview" replace />} />
      </Route>
    </Routes>
  );
}

export default App;

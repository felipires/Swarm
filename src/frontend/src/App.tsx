import { Navigate, Route, Routes } from "react-router-dom";
import { AppShell } from "./components/shell/AppShell";
import { RouteView } from "./components/shell/RouteView";
import { ObservabilityPage } from "./pages/Observability";
import { OverviewPage } from "./pages/Overview";
import { SettingsPage } from "./pages/Settings";
import { TasksPage } from "./pages/Tasks";
import { WorkflowsPage } from "./pages/Workflows";

function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<Navigate to="/overview" replace />} />
        <Route path="overview" element={<RouteView Page={OverviewPage} />} />
        <Route path="workflows" element={<RouteView Page={WorkflowsPage} />} />
        <Route path="tasks" element={<RouteView Page={TasksPage} />} />
        <Route path="observability" element={<RouteView Page={ObservabilityPage} />} />
        <Route path="settings" element={<RouteView Page={SettingsPage} />} />
        <Route path="*" element={<Navigate to="/overview" replace />} />
      </Route>
    </Routes>
  );
}

export default App;

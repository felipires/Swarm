import { useEffect, useState } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { useClusterPulse } from "../../hooks/useClusterPulse";
import type { NavRoute } from "../../types/navigation";
import { PRIMARY_NAV, SETTINGS_NAV } from "../../types/navigation";
import type { ShellContext } from "./RouteView";
import { Sidebar } from "./Sidebar";
import { StatusStrip } from "./StatusStrip";

const LG_BREAKPOINT = 1024;
const KNOWN_ROUTES = [...PRIMARY_NAV, SETTINGS_NAV];

function routeFromPath(pathname: string): NavRoute {
  const match = KNOWN_ROUTES.find((item) => pathname.startsWith(item.path));
  return match?.id ?? "overview";
}

export function AppShell() {
  const location = useLocation();
  const navigate = useNavigate();
  const route = routeFromPath(location.pathname);

  const [collapsed, setCollapsed] = useState(false);
  const [searchQuery, setSearchQuery] = useState("");
  const pulse = useClusterPulse();

  useEffect(() => {
    const mq = window.matchMedia(`(max-width: ${LG_BREAKPOINT - 1}px)`);
    const apply = () => setCollapsed(mq.matches);
    apply();
    mq.addEventListener("change", apply);
    return () => mq.removeEventListener("change", apply);
  }, []);

  const context: ShellContext = { pulse };

  return (
    <div className="flex h-screen min-h-0 bg-[var(--swarm-bg)] text-[var(--swarm-ink)]">
      <Sidebar
        collapsed={collapsed}
        onToggleCollapse={() => setCollapsed((c) => !c)}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <StatusStrip
          alertCount={pulse.alertCount}
          connection={pulse.connection}
          onlineCount={pulse.onlineCount}
          totalNodes={pulse.totalNodes}
          searchQuery={searchQuery}
          onSearchChange={setSearchQuery}
          onAlertsClick={() => {
            const params = new URLSearchParams({ q: "level:>warn" });
            navigate(`/observability?${params}`);
          }}
        />

        <main
          id="main-content"
          className="min-h-0 flex-1 overflow-y-auto bg-[var(--swarm-bg)]"
          role="main"
          aria-label={route}
        >
          <Outlet context={context} />
        </main>
      </div>
    </div>
  );
}

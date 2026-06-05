import { useEffect, useState } from "react";
import { useClusterPulse } from "../../hooks/useClusterPulse";
import type { NavRoute } from "../../types/navigation";
import { PlaceholderView } from "./PlaceholderView";
import { Sidebar } from "./Sidebar";
import { StatusStrip } from "./StatusStrip";

const LG_BREAKPOINT = 1024;

export function AppShell() {
  const [route, setRoute] = useState<NavRoute>("overview");
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

  return (
    <div className="flex h-screen min-h-0 bg-[var(--swarm-bg)] text-[var(--swarm-ink)]">
      <Sidebar
        active={route}
        collapsed={collapsed}
        onNavigate={setRoute}
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
          onAlertsClick={() => setRoute("observability")}
        />

        <main
          id="main-content"
          className="min-h-0 flex-1 overflow-y-auto bg-[var(--swarm-bg)]"
          role="main"
          aria-label={route}
        >
          <PlaceholderView route={route} connection={pulse.connection} />
        </main>
      </div>
    </div>
  );
}

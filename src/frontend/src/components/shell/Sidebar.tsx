import { useEffect, useRef } from "react";
import { NavLink } from "react-router-dom";
import type { NavRoute } from "../../types/navigation";
import { PRIMARY_NAV, SETTINGS_NAV } from "../../types/navigation";
import {
  IconChevron,
  IconObservability,
  IconOverview,
  IconSettings,
  IconTasks,
  IconWorkflows,
} from "./icons";

const ICONS = {
  overview: IconOverview,
  workflows: IconWorkflows,
  tasks: IconTasks,
  observability: IconObservability,
  settings: IconSettings,
} as const;

interface SidebarProps {
  collapsed: boolean;
  onToggleCollapse: () => void;
}

export function Sidebar({ collapsed, onToggleCollapse }: SidebarProps) {
  const navRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const nav = navRef.current;
    if (!nav) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      const items = Array.from(nav.querySelectorAll<HTMLElement>('[role="menuitem"]'));
      const index = items.findIndex((b) => b === document.activeElement);
      if (index === -1) return;

      if (e.key === "ArrowDown") {
        e.preventDefault();
        items[(index + 1) % items.length]?.focus();
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        items[(index - 1 + items.length) % items.length]?.focus();
      } else if (e.key === "Home") {
        e.preventDefault();
        items[0]?.focus();
      } else if (e.key === "End") {
        e.preventDefault();
        items[items.length - 1]?.focus();
      }
    };

    nav.addEventListener("keydown", handleKeyDown);
    return () => nav.removeEventListener("keydown", handleKeyDown);
  }, []);

  const renderItem = (id: NavRoute, label: string, path: string) => {
    const Icon = ICONS[id];

    return (
      <NavLink
        key={id}
        to={path}
        role="menuitem"
        title={collapsed ? label : undefined}
        className={({ isActive }) =>
          [
            "group flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm font-medium transition-colors",
            "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]",
            isActive
              ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
              : "text-[var(--swarm-muted)] hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)]",
          ].join(" ")
        }
        style={{ transitionDuration: "var(--swarm-duration)", transitionTimingFunction: "var(--swarm-ease-out)" }}
      >
        {({ isActive }) => (
          <>
            <span
              className={[
                "flex shrink-0 items-center justify-center rounded-md p-1",
                isActive
                  ? "text-[var(--swarm-primary)]"
                  : "text-[var(--swarm-muted)] group-hover:text-[var(--swarm-ink)]",
              ].join(" ")}
            >
              <Icon />
            </span>
            {!collapsed && <span className="truncate">{label}</span>}
          </>
        )}
      </NavLink>
    );
  };

  return (
    <aside
      className={[
        "flex h-full shrink-0 flex-col border-r border-[var(--swarm-border)] bg-[var(--swarm-chrome)]",
        collapsed ? "w-[4.25rem]" : "w-56",
      ].join(" ")}
      style={{ transition: "width var(--swarm-duration) var(--swarm-ease-out)" }}
    >
      <div className={`flex items-center gap-2 border-b border-[var(--swarm-border)] px-3 py-4 ${collapsed ? "justify-center" : ""}`}>
        <div
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-[var(--swarm-primary)] text-[var(--swarm-on-primary)]"
          aria-hidden
        >
          <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
            <path d="M3 8.5L6.5 5l2 2.5L11 4l2 2v6.5a1 1 0 01-1 1H4a1 1 0 01-1-1V8.5z" />
          </svg>
        </div>
        {!collapsed && (
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold text-[var(--swarm-ink)]" style={{ textWrap: "balance" }}>
              Swarm
            </p>
            <p className="truncate text-xs text-[var(--swarm-muted)]">Orchestrator</p>
          </div>
        )}
      </div>

      <nav ref={navRef} aria-label="Primary" role="menu" className="flex flex-1 flex-col gap-1 px-2 py-3">
        {PRIMARY_NAV.map((item) => renderItem(item.id, item.label, item.path))}
      </nav>

      <div className="mt-auto border-t border-[var(--swarm-border)] px-2 py-3">
        <nav aria-label="Settings" role="menu">
          {renderItem(SETTINGS_NAV.id, SETTINGS_NAV.label, SETTINGS_NAV.path)}
        </nav>
        <button
          type="button"
          onClick={onToggleCollapse}
          aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          className="mt-2 flex w-full items-center justify-center rounded-md p-2 text-[var(--swarm-muted)] hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]"
        >
          <IconChevron direction={collapsed ? "right" : "left"} />
        </button>
      </div>
    </aside>
  );
}

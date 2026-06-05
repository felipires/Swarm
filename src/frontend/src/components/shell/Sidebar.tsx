import { useEffect, useRef } from "react";
import type { NavRoute } from "../../types/navigation";
import { PRIMARY_NAV, SETTINGS_NAV } from "../../types/navigation";
import {
  IconChevron,
  IconObservability,
  IconOverview,
  IconSettings,
  IconWorkflows,
} from "./icons";

const ICONS = {
  overview: IconOverview,
  workflows: IconWorkflows,
  observability: IconObservability,
  settings: IconSettings,
} as const;

interface SidebarProps {
  active: NavRoute;
  collapsed: boolean;
  onNavigate: (route: NavRoute) => void;
  onToggleCollapse: () => void;
}

export function Sidebar({ active, collapsed, onNavigate, onToggleCollapse }: SidebarProps) {
  const navRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const nav = navRef.current;
    if (!nav) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      const buttons = Array.from(nav.querySelectorAll<HTMLButtonElement>('[role="menuitem"]'));
      const index = buttons.findIndex((b) => b === document.activeElement);
      if (index === -1) return;

      if (e.key === "ArrowDown") {
        e.preventDefault();
        buttons[(index + 1) % buttons.length]?.focus();
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        buttons[(index - 1 + buttons.length) % buttons.length]?.focus();
      } else if (e.key === "Home") {
        e.preventDefault();
        buttons[0]?.focus();
      } else if (e.key === "End") {
        e.preventDefault();
        buttons[buttons.length - 1]?.focus();
      }
    };

    nav.addEventListener("keydown", handleKeyDown);
    return () => nav.removeEventListener("keydown", handleKeyDown);
  }, []);

  const renderItem = (id: NavRoute, label: string) => {
    const Icon = ICONS[id];
    const isActive = active === id;

    return (
      <button
        key={id}
        type="button"
        role="menuitem"
        aria-current={isActive ? "page" : undefined}
        title={collapsed ? label : undefined}
        onClick={() => onNavigate(id)}
        className={[
          "group flex w-full items-center gap-3 rounded-md px-3 py-2 text-left text-sm font-medium transition-colors",
          "focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--swarm-focus)]",
          isActive
            ? "bg-[var(--swarm-primary-subtle)] text-[var(--swarm-ink)]"
            : "text-[var(--swarm-muted)] hover:bg-[var(--swarm-surface-raised)] hover:text-[var(--swarm-ink)]",
        ].join(" ")}
        style={{ transitionDuration: "var(--swarm-duration)", transitionTimingFunction: "var(--swarm-ease-out)" }}
      >
        <span
          className={[
            "flex shrink-0 items-center justify-center rounded-md p-1",
            isActive ? "text-[var(--swarm-primary)]" : "text-[var(--swarm-muted)] group-hover:text-[var(--swarm-ink)]",
          ].join(" ")}
        >
          <Icon />
        </span>
        {!collapsed && <span className="truncate">{label}</span>}
      </button>
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
        {PRIMARY_NAV.map((item) => renderItem(item.id, item.label))}
      </nav>

      <div className="mt-auto border-t border-[var(--swarm-border)] px-2 py-3">
        <nav aria-label="Settings" role="menu">
          {renderItem(SETTINGS_NAV.id, SETTINGS_NAV.label)}
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

import { useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import {
  Activity,
  Building2,
  KeyRound,
  Link2,
  PanelLeftClose,
  PanelLeftOpen,
  Plug,
  ScrollText,
  ShieldCheck,
  Users,
} from "lucide-react";
import { cn } from "@/lib/cn";
import { UserMenu } from "@/components/layout/UserMenu";

const COLLAPSE_STORAGE_KEY = "forgevault-sidebar-collapsed";

// Grouped like ForgeHub's NAV_SECTIONS (components/layout/navSections.ts) — a labeled
// header per section, though without that file's per-section collapse, which isn't worth
// the extra state for a sidebar this short.
const NAV_SECTIONS = [
  {
    label: "General",
    items: [{ to: "/overview", label: "Overview", icon: Activity }],
  },
  {
    label: "Vault",
    items: [{ to: "/organizations", label: "Organizations", icon: Building2 }],
  },
  {
    label: "Identity",
    items: [
      { to: "/users", label: "Users", icon: Users },
      { to: "/service-accounts", label: "Service Accounts", icon: KeyRound },
      { to: "/access", label: "Access & Roles", icon: ShieldCheck },
    ],
  },
  {
    label: "MCP",
    items: [
      { to: "/mcp-servers", label: "MCP Servers", icon: Plug },
      { to: "/mcp-assignments", label: "MCP Assignments", icon: Link2 },
    ],
  },
  {
    label: "Governance",
    items: [{ to: "/audit", label: "Audit", icon: ScrollText }],
  },
];

export function Sidebar() {
  // Same convention as ForgeHub's Sidebar (localStorage-persisted icon-rail toggle), applied
  // to this sidebar's own shape — full-width labeled sections collapse to an icon-only rail.
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_STORAGE_KEY) === "1");

  useEffect(() => {
    localStorage.setItem(COLLAPSE_STORAGE_KEY, collapsed ? "1" : "0");
  }, [collapsed]);

  return (
    <aside
      className={cn(
        "flex h-screen shrink-0 flex-col border-r border-vault-surface-border bg-vault-bg-deep transition-[width] duration-150",
        collapsed ? "w-16" : "w-56",
      )}
    >
      <div
        className={cn(
          "flex h-16 items-center border-b border-vault-surface-border",
          collapsed ? "justify-center px-2" : "justify-between px-4",
        )}
      >
        {collapsed ? (
          // Icon-rail mode: the logo itself is the expand trigger — hover swaps the mark
          // for the expand icon, same affordance ForgeHub's Sidebar uses for its own
          // collapsed logo button.
          <button
            type="button"
            onClick={() => setCollapsed(false)}
            aria-label="Expand sidebar"
            className="group relative flex h-10 w-10 items-center justify-center rounded-md hover:bg-vault-surface-dim/60"
          >
            <img src="/forgevault-icon.svg" alt="" className="h-7 w-7 shrink-0 transition-opacity group-hover:opacity-0" />
            <PanelLeftOpen className="absolute h-4 w-4 text-slate-300 opacity-0 transition-opacity group-hover:opacity-100" />
            <span className="pointer-events-none absolute left-full top-1/2 z-20 ml-2 -translate-y-1/2 whitespace-nowrap rounded-md border border-vault-surface-border bg-vault-bg px-2 py-1 text-xs font-medium text-slate-100 opacity-0 shadow-vault-glow transition-opacity group-hover:opacity-100">
              Expand sidebar
            </span>
          </button>
        ) : (
          <>
            <div className="flex min-w-0 items-center gap-2">
              <img src="/forgevault-icon.svg" alt="ForgeVault" className="h-7 w-7 shrink-0" />
              <span className="truncate text-sm font-semibold tracking-wide text-slate-100">ForgeVault</span>
            </div>
            <button
              type="button"
              onClick={() => setCollapsed(true)}
              aria-label="Collapse sidebar"
              title="Collapse sidebar"
              className="rounded-md p-1.5 text-slate-400 transition-colors hover:bg-vault-surface-dim/60 hover:text-slate-200"
            >
              <PanelLeftClose className="h-4 w-4" />
            </button>
          </>
        )}
      </div>

      <nav className="flex flex-1 flex-col gap-4 overflow-y-auto px-2">
        {NAV_SECTIONS.map((section) => (
          <div key={section.label} className="flex flex-col gap-1">
            {!collapsed && <p className="px-3 text-[10px] font-semibold uppercase tracking-wider text-slate-500">{section.label}</p>}
            {section.items.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                title={collapsed ? label : undefined}
                className={({ isActive }) =>
                  cn(
                    "flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors",
                    collapsed && "justify-center px-2",
                    isActive
                      ? "bg-vault-surface-dim text-vault-accent-bright"
                      : "text-slate-400 hover:bg-vault-surface-dim/50 hover:text-slate-200",
                  )
                }
              >
                <Icon className="h-4 w-4 shrink-0" />
                {!collapsed && label}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      <UserMenu collapsed={collapsed} />
    </aside>
  );
}

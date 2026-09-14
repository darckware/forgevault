import { NavLink } from "react-router-dom";
import { Activity, Building2, KeyRound, Link2, Plug, ScrollText, ShieldCheck, Users } from "lucide-react";
import { cn } from "@/lib/cn";
import { UserMenu } from "@/components/layout/UserMenu";

const NAV_ITEMS = [
  { to: "/overview", label: "Overview", icon: Activity },
  { to: "/organizations", label: "Organizations", icon: Building2 },
  { to: "/users", label: "Users", icon: Users },
  { to: "/service-accounts", label: "Service Accounts", icon: KeyRound },
  { to: "/access", label: "Access & Roles", icon: ShieldCheck },
  { to: "/mcp-servers", label: "MCP Servers", icon: Plug },
  { to: "/mcp-assignments", label: "MCP Assignments", icon: Link2 },
  { to: "/audit", label: "Audit", icon: ScrollText },
];

export function Sidebar() {
  return (
    <aside className="flex h-screen w-56 shrink-0 flex-col border-r border-vault-surface-border bg-vault-bg-deep">
      <div className="flex items-center gap-2 px-4 py-5">
        <img src="/forgevault-icon.svg" alt="ForgeVault" className="h-7 w-7" />
        <span className="text-sm font-semibold tracking-wide text-slate-100">ForgeVault</span>
      </div>
      <nav className="flex flex-1 flex-col gap-1 overflow-y-auto px-2">
        {NAV_ITEMS.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors",
                isActive
                  ? "bg-vault-surface-dim text-vault-accent-bright"
                  : "text-slate-400 hover:bg-vault-surface-dim/50 hover:text-slate-200",
              )
            }
          >
            <Icon className="h-4 w-4" />
            {label}
          </NavLink>
        ))}
      </nav>
      <UserMenu />
    </aside>
  );
}

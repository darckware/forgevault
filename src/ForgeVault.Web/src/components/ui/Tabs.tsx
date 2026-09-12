import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface Tab {
  id: string;
  label: string;
}

export interface TabsProps {
  tabs: Tab[];
  activeId: string;
  onChange: (id: string) => void;
  children: ReactNode;
}

export function Tabs({ tabs, activeId, onChange, children }: TabsProps) {
  return (
    <div>
      <div className="mb-4 flex gap-1 border-b border-vault-surface-border">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => onChange(tab.id)}
            className={cn(
              "border-b-2 px-4 py-2 text-sm font-medium transition-colors",
              activeId === tab.id
                ? "border-vault-accent text-vault-accent-bright"
                : "border-transparent text-slate-500 hover:text-slate-300",
            )}
          >
            {tab.label}
          </button>
        ))}
      </div>
      {children}
    </div>
  );
}

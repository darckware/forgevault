import type { ReactNode } from "react";
import { cn } from "@/lib/cn";

export interface CardProps {
  title?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}

export function Card({ title, actions, children, className }: CardProps) {
  return (
    <div className={cn("rounded-lg border border-vault-surface-border bg-vault-surface-dim/60 p-5", className)}>
      {(title || actions) && (
        <div className="mb-4 flex items-center justify-between">
          {title && <h2 className="text-sm font-semibold uppercase tracking-wide text-vault-accent-bright">{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </div>
  );
}

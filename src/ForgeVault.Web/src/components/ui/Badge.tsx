import type { ReactNode } from "react";
import { Lock, LockOpen } from "lucide-react";
import { cn } from "@/lib/cn";
import type { BadgeTone } from "@/lib/format";

const TONE_STYLES: Record<BadgeTone, string> = {
  neutral: "bg-slate-700/40 text-slate-300 border-slate-600/50",
  success: "bg-vault-accent-dark/20 text-vault-accent-bright border-vault-accent-dark/50",
  warning: "bg-vault-warning/20 text-amber-300 border-vault-warning/40",
  danger: "bg-vault-danger/20 text-rose-300 border-vault-danger/40",
  info: "bg-sky-500/20 text-sky-300 border-sky-500/40",
};

export interface BadgeProps {
  tone: BadgeTone;
  children: ReactNode;
  showLockIcon?: boolean;
}

// Status badges follow one deterministic tone scale everywhere (see lib/format.ts's
// statusTone), and pair the color with a lock/unlock icon so status is legible without
// relying purely on hue.
export function Badge({ tone, children, showLockIcon }: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium",
        TONE_STYLES[tone],
      )}
    >
      {showLockIcon && (tone === "danger" ? <Lock className="h-3 w-3" /> : <LockOpen className="h-3 w-3" />)}
      {children}
    </span>
  );
}

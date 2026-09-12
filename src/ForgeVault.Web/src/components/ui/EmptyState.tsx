import type { ReactNode } from "react";
import { ShieldOff } from "lucide-react";

export function EmptyState({ message, action }: { message: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 py-12 text-center">
      <ShieldOff className="h-8 w-8 text-slate-600" />
      <p className="text-sm text-slate-500">{message}</p>
      {action}
    </div>
  );
}

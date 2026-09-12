import { useState } from "react";
import { Check, Copy } from "lucide-react";
import { truncateId } from "@/lib/format";
import { cn } from "@/lib/cn";

export interface MonoIdProps {
  value: string;
  label?: string;
  truncate?: boolean;
  copyable?: boolean;
  className?: string;
}

// The single place secret names, GUIDs, and revealed values get their "vault ledger" look —
// used everywhere an identifier or secret material appears in a table or detail view.
export function MonoId({ value, label, truncate = true, copyable = true, className }: MonoIdProps) {
  const [copied, setCopied] = useState(false);
  const display = truncate ? truncateId(value) : value;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard API unavailable (non-HTTPS/localhost context) — silently no-op, the value
      // is still visible and selectable by hand.
    }
  };

  return (
    <span className={cn("inline-flex items-center gap-1.5", className)}>
      {label && <span className="text-xs text-slate-500">{label}</span>}
      <span title={value} className="rounded bg-vault-ink/40 px-1.5 py-0.5 font-mono text-xs text-vault-accent-bright">
        {display}
      </span>
      {copyable && (
        <button
          type="button"
          onClick={handleCopy}
          className="text-slate-500 hover:text-vault-accent-bright"
          aria-label="Copy"
        >
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      )}
    </span>
  );
}

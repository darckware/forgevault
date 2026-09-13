import { type TextareaHTMLAttributes, forwardRef } from "react";
import { cn } from "@/lib/cn";

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string;
  error?: string;
  hint?: string;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(
  ({ label, error, hint, className, id, rows = 3, ...props }, ref) => {
    const textareaId = id ?? label.toLowerCase().replace(/\s+/g, "-");
    return (
      <div className="flex flex-col gap-1">
        <label htmlFor={textareaId} className="text-xs font-medium text-slate-400">
          {label}
        </label>
        <textarea
          ref={ref}
          id={textareaId}
          rows={rows}
          className={cn(
            "rounded-md border border-vault-surface-border bg-vault-bg px-3 py-2 font-mono text-sm text-slate-100",
            "placeholder:font-sans placeholder:text-slate-600 focus:border-vault-accent focus:outline-none focus:ring-1 focus:ring-vault-accent",
            error && "border-vault-danger focus:border-vault-danger focus:ring-vault-danger",
            className,
          )}
          {...props}
        />
        {hint && !error && <p className="text-xs text-slate-500">{hint}</p>}
        {error && <p className="text-xs text-vault-danger">{error}</p>}
      </div>
    );
  },
);
Textarea.displayName = "Textarea";

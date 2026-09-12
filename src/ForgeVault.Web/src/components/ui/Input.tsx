import { type InputHTMLAttributes, forwardRef } from "react";
import { cn } from "@/lib/cn";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  hint?: string;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(({ label, error, hint, className, id, ...props }, ref) => {
  const inputId = id ?? label.toLowerCase().replace(/\s+/g, "-");
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={inputId} className="text-xs font-medium text-slate-400">
        {label}
      </label>
      <input
        ref={ref}
        id={inputId}
        className={cn(
          "rounded-md border border-vault-surface-border bg-vault-bg px-3 py-2 text-sm text-slate-100",
          "placeholder:text-slate-600 focus:border-vault-accent focus:outline-none focus:ring-1 focus:ring-vault-accent",
          error && "border-vault-danger focus:border-vault-danger focus:ring-vault-danger",
          className,
        )}
        {...props}
      />
      {hint && !error && <p className="text-xs text-slate-500">{hint}</p>}
      {error && <p className="text-xs text-vault-danger">{error}</p>}
    </div>
  );
});
Input.displayName = "Input";

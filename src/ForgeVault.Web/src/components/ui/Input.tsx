import { type InputHTMLAttributes, type ReactNode, forwardRef } from "react";
import { cn } from "@/lib/cn";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  hint?: string;
  // Positioned relative to the input box itself (top-1/2 -translate-y-1/2 inside a wrapper
  // around only the <input>, not the label above it) — always truly centered regardless of
  // label length/wrapping, unlike a hardcoded pixel offset guessed from outside this
  // component (the bug this replaced: LoginPage.tsx used to position icons with
  // `absolute top-[34px]` on a sibling of the whole labeled field).
  leadingIcon?: ReactNode;
  trailingElement?: ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ label, error, hint, className, id, leadingIcon, trailingElement, ...props }, ref) => {
    const inputId = id ?? label.toLowerCase().replace(/\s+/g, "-");
    return (
      <div className="flex flex-col gap-1">
        <label htmlFor={inputId} className="text-xs font-medium text-slate-400">
          {label}
        </label>
        <div className="relative">
          {leadingIcon && (
            <span className="pointer-events-none absolute left-3 top-1/2 flex -translate-y-1/2 items-center text-slate-500">
              {leadingIcon}
            </span>
          )}
          <input
            ref={ref}
            id={inputId}
            className={cn(
              "w-full rounded-md border border-vault-surface-border bg-vault-bg px-3 py-2 text-sm text-slate-100",
              leadingIcon && "pl-9",
              trailingElement && "pr-9",
              "placeholder:text-slate-600 focus:border-vault-accent focus:outline-none focus:ring-1 focus:ring-vault-accent",
              error && "border-vault-danger focus:border-vault-danger focus:ring-vault-danger",
              className,
            )}
            {...props}
          />
          {trailingElement && (
            <span className="absolute right-2.5 top-1/2 flex -translate-y-1/2 items-center">{trailingElement}</span>
          )}
        </div>
        {hint && !error && <p className="text-xs text-slate-500">{hint}</p>}
        {error && <p className="text-xs text-vault-danger">{error}</p>}
      </div>
    );
  },
);
Input.displayName = "Input";

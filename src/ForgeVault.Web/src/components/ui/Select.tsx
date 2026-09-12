import { type SelectHTMLAttributes, forwardRef } from "react";
import { cn } from "@/lib/cn";

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  error?: string;
  options: SelectOption[];
  placeholder?: string;
}

// Plain native <select> — no custom listbox, keeps v1 simple and accessible for free.
export const Select = forwardRef<HTMLSelectElement, SelectProps>(
  ({ label, error, options, placeholder, className, id, ...props }, ref) => {
    const selectId = id ?? label.toLowerCase().replace(/\s+/g, "-");
    return (
      <div className="flex flex-col gap-1">
        <label htmlFor={selectId} className="text-xs font-medium text-slate-400">
          {label}
        </label>
        <select
          ref={ref}
          id={selectId}
          className={cn(
            "rounded-md border border-vault-surface-border bg-vault-bg px-3 py-2 text-sm text-slate-100",
            "focus:border-vault-accent focus:outline-none focus:ring-1 focus:ring-vault-accent",
            error && "border-vault-danger",
            className,
          )}
          {...props}
        >
          {placeholder && (
            <option value="" disabled>
              {placeholder}
            </option>
          )}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        {error && <p className="text-xs text-vault-danger">{error}</p>}
      </div>
    );
  },
);
Select.displayName = "Select";

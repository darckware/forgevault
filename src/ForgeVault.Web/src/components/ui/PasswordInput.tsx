import { forwardRef, useRef, useState } from "react";
import { Check, Copy, Eye, EyeOff } from "lucide-react";
import { Input, type InputProps } from "@/components/ui/Input";
import { cn } from "@/lib/cn";

export type PasswordInputProps = Omit<InputProps, "type" | "trailingElement">;

// Same show/hide toggle as LoginPage's password field, plus a copy-to-clipboard button — for
// secret creation/rotation forms, where the operator often wants to double-check or reuse
// exactly what they just typed. Value never leaves the browser except via the Clipboard API
// call the user explicitly triggers by clicking.
export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(
  ({ className, ...props }, forwardedRef) => {
    const [visible, setVisible] = useState(false);
    const [copied, setCopied] = useState(false);
    const innerRef = useRef<HTMLInputElement | null>(null);

    const setRefs = (el: HTMLInputElement | null) => {
      innerRef.current = el;
      if (typeof forwardedRef === "function") {
        forwardedRef(el);
      } else if (forwardedRef) {
        forwardedRef.current = el;
      }
    };

    const handleCopy = async () => {
      const value = innerRef.current?.value ?? "";
      if (!value) {
        return;
      }
      try {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
      } catch {
        // Clipboard API unavailable (non-HTTPS/localhost context) — value is still visible
        // and selectable by hand once toggled.
      }
    };

    return (
      <Input
        ref={setRefs}
        type={visible ? "text" : "password"}
        className={cn("pr-16", className)}
        trailingElement={
          <div className="flex items-center gap-1.5">
            <button
              type="button"
              onClick={handleCopy}
              className="text-slate-500 hover:text-vault-accent-bright"
              tabIndex={-1}
              aria-label="Copy value"
            >
              {copied ? <Check className="h-4 w-4" /> : <Copy className="h-4 w-4" />}
            </button>
            <button
              type="button"
              onClick={() => setVisible((v) => !v)}
              className="text-slate-500 hover:text-slate-200"
              tabIndex={-1}
              aria-label={visible ? "Hide value" : "Show value"}
            >
              {visible ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        }
        {...props}
      />
    );
  },
);
PasswordInput.displayName = "PasswordInput";

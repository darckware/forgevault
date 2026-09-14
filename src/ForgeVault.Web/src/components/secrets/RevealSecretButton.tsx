import { useEffect, useRef, useState } from "react";
import { Copy, Lock, ShieldAlert, Unlock } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Modal } from "@/components/ui/Modal";
import { useRevealSecret } from "@/hooks/useSecrets";
import { decodeStructuredValue, isStructuredSecretType } from "@/lib/secretValue";
import type { SecretType } from "@/types/api";

const REVEAL_SECONDS = 20;

// The signature "vault door" interaction: closed lock -> confirm -> loading -> open lock
// with a teal glow and a countdown -> auto-reseals. The plaintext lives only in this
// component's own state for the countdown's duration, never in the react-query cache.
export function RevealSecretButton({
  secretId,
  secretName,
  secretType,
}: {
  secretId: string;
  secretName: string;
  secretType?: SecretType;
}) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [revealedValue, setRevealedValue] = useState<string | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(REVEAL_SECONDS);
  const reveal = useRevealSecret(secretId);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const clearTimer = () => {
    if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  };

  const hide = () => {
    clearTimer();
    setRevealedValue(null);
    reveal.reset();
  };

  useEffect(() => clearTimer, []);

  const handleConfirm = async () => {
    setConfirmOpen(false);
    const envelope = await reveal.mutateAsync();
    const value = envelope.credentials?.value ?? "";
    setRevealedValue(value);
    setSecondsLeft(REVEAL_SECONDS);

    clearTimer();
    timerRef.current = setInterval(() => {
      setSecondsLeft((s) => {
        if (s <= 1) {
          hide();
          return REVEAL_SECONDS;
        }
        return s - 1;
      });
    }, 1000);
  };

  if (revealedValue !== null) {
    const structured = secretType && isStructuredSecretType(secretType) ? decodeStructuredValue(secretType, revealedValue) : null;

    return (
      <div className="flex flex-col gap-2 rounded-md border border-vault-accent-dark bg-vault-ink/40 px-3 py-2 shadow-vault-glow">
        <div className="flex items-center gap-2">
          <Unlock className="h-4 w-4 shrink-0 text-vault-accent-bright" />
          {structured ? (
            <div className="flex flex-col gap-1">
              {Object.entries(structured).map(([key, val]) => (
                <div key={key} className="flex items-center gap-2">
                  <span className="w-20 shrink-0 text-xs uppercase text-slate-500">{key}</span>
                  <code className="font-mono text-sm text-vault-accent-bright">{val}</code>
                  <button
                    onClick={() => navigator.clipboard.writeText(val).catch(() => undefined)}
                    className="text-slate-400 hover:text-vault-accent-bright"
                    aria-label={`Copy ${key}`}
                  >
                    <Copy className="h-3.5 w-3.5" />
                  </button>
                </div>
              ))}
            </div>
          ) : (
            <>
              <code className="font-mono text-sm text-vault-accent-bright">{revealedValue}</code>
              <button
                onClick={() => navigator.clipboard.writeText(revealedValue).catch(() => undefined)}
                className="text-slate-400 hover:text-vault-accent-bright"
                aria-label="Copy value"
              >
                <Copy className="h-3.5 w-3.5" />
              </button>
            </>
          )}
          <span className="ml-2 text-xs text-slate-500">{secondsLeft}s</span>
          <Button variant="ghost" size="sm" onClick={hide}>
            Hide now
          </Button>
        </div>
      </div>
    );
  }

  return (
    <>
      <Button variant="secondary" onClick={() => setConfirmOpen(true)} isLoading={reveal.isPending}>
        <Lock className="h-4 w-4" />
        Reveal
      </Button>

      <Modal open={confirmOpen} onClose={() => setConfirmOpen(false)} title="Reveal secret value">
        <div className="flex flex-col gap-3">
          <div className="flex gap-2 rounded-md bg-vault-warning/10 p-3 text-sm text-amber-800 dark:text-amber-200">
            <ShieldAlert className="h-4 w-4 shrink-0" />
            <p>
              You are about to reveal <span className="font-mono text-amber-700 dark:text-amber-100">{secretName}</span>&apos;s live value.
              This action is logged in the audit trail.
            </p>
          </div>
          {reveal.isError && <p className="text-sm text-vault-danger">{(reveal.error as Error).message}</p>}
        </div>
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirmOpen(false)}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleConfirm}>
            Reveal
          </Button>
        </div>
      </Modal>
    </>
  );
}

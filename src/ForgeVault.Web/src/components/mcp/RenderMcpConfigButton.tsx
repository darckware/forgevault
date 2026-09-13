import { useEffect, useRef, useState } from "react";
import { Copy, Lock, ShieldAlert, Unlock } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Modal } from "@/components/ui/Modal";
import { useRenderMcpAssignment } from "@/hooks/useMcpAssignments";
import { ApiError } from "@/lib/api";

const REVEAL_SECONDS = 20;

// Same "vault door" interaction as RevealSecretButton, applied to a resolved MCP config
// block — it can contain multiple decrypted secret values (one per env/header entry), so it
// gets the same masked-by-default, explicit-reveal, auto-reseal treatment.
export function RenderMcpConfigButton({ assignmentId }: { assignmentId: string }) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [renderedJson, setRenderedJson] = useState<string | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(REVEAL_SECONDS);
  const render = useRenderMcpAssignment(assignmentId);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const clearTimer = () => {
    if (timerRef.current) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  };

  const hide = () => {
    clearTimer();
    setRenderedJson(null);
    render.reset();
  };

  useEffect(() => clearTimer, []);

  const handleConfirm = async () => {
    // Keep the confirmation modal open until the mutation settles — closing it first (as this
    // used to) discards the only surface that renders render.isError, so a failure (e.g. a
    // revoked assignment) left the admin with no feedback at all, just a button that stopped
    // loading.
    try {
      const config = await render.mutateAsync();
      setConfirmOpen(false);
      setRenderedJson(JSON.stringify(config, null, 2));
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
    } catch {
      // render.isError / render.error already reflect this — the modal stays open to show it.
    }
  };

  return (
    <>
      <Button variant="secondary" size="sm" onClick={() => setConfirmOpen(true)} isLoading={render.isPending}>
        <Lock className="h-3.5 w-3.5" />
        Render config
      </Button>

      <Modal open={confirmOpen} onClose={() => setConfirmOpen(false)} title="Render MCP config">
        <div className="flex flex-col gap-3">
          <div className="flex gap-2 rounded-md bg-vault-warning/10 p-3 text-sm text-amber-200">
            <ShieldAlert className="h-4 w-4 shrink-0" />
            <p>Every referenced secret in this assignment will be decrypted. This action is logged in the audit trail.</p>
          </div>
          {render.isError && (
            <p className="text-sm text-vault-danger">
              {render.error instanceof ApiError ? (render.error.body?.error ?? render.error.message) : "Failed to render"}
            </p>
          )}
        </div>
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirmOpen(false)}>
            Cancel
          </Button>
          <Button variant="primary" onClick={handleConfirm}>
            Render
          </Button>
        </div>
      </Modal>

      <Modal open={!!renderedJson} onClose={hide} title="Resolved config">
        <div className="flex flex-col gap-3">
          <div className="flex items-center gap-2 rounded-md border border-vault-accent-dark bg-vault-ink/40 px-3 py-2 shadow-vault-glow">
            <Unlock className="h-4 w-4 shrink-0 text-vault-accent-bright" />
            <span className="text-xs text-slate-500">Auto-hides in {secondsLeft}s</span>
            <button
              onClick={() => navigator.clipboard.writeText(renderedJson ?? "").catch(() => undefined)}
              className="ml-auto text-slate-400 hover:text-vault-accent-bright"
              aria-label="Copy config"
            >
              <Copy className="h-3.5 w-3.5" />
            </button>
          </div>
          <pre className="max-h-80 overflow-auto rounded-md bg-vault-ink/40 p-3 font-mono text-xs text-vault-accent-bright">
            {renderedJson}
          </pre>
          <Button variant="ghost" size="sm" onClick={hide} className="self-end">
            Hide now
          </Button>
        </div>
      </Modal>
    </>
  );
}

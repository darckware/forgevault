import { LogOut } from "lucide-react";
import { useMe, useLogout } from "@/hooks/useAuth";
import { Button } from "@/components/ui/Button";

export function Topbar() {
  const { data: me } = useMe();
  const logout = useLogout();

  return (
    <header className="flex items-center justify-between border-b border-vault-surface-border px-6 py-3">
      <div />
      <div className="flex items-center gap-3">
        {me && <span className="text-sm text-slate-400">{me.email}</span>}
        <Button variant="ghost" size="sm" onClick={logout}>
          <LogOut className="h-4 w-4" />
          Sair
        </Button>
      </div>
    </header>
  );
}

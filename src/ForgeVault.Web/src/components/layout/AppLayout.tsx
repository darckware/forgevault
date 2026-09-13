import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";

// Identity (email/avatar) and account actions (profile, change password, admin, logout)
// live in the Sidebar's bottom UserMenu now — same placement ForgeHub uses — so there's no
// separate top bar to render here anymore.
export function AppLayout() {
  return (
    <div className="flex h-full min-h-screen bg-vault-bg text-slate-200">
      <Sidebar />
      <main className="flex-1 overflow-y-auto p-6">
        <Outlet />
      </main>
    </div>
  );
}

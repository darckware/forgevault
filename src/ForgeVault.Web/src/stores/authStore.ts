import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { MeResponse } from "@/types/api";

interface Session {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

interface AuthState {
  accessToken: string | null;
  refreshToken: string | null;
  expiresAt: string | null;
  user: MeResponse | null;
  setSession: (session: Session) => void;
  setUser: (user: MeResponse) => void;
  clear: () => void;
}

// Read/written both from React (via the hook) and from lib/api.ts's fetch wrapper outside
// the component tree (via authStore.getState()/.setState()) — a plain zustand store, not
// React context, so the refresh logic in api.ts can reach it without a provider.
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      expiresAt: null,
      user: null,
      setSession: (session) =>
        set({
          accessToken: session.accessToken,
          refreshToken: session.refreshToken,
          expiresAt: session.expiresAt,
        }),
      setUser: (user) => set({ user }),
      clear: () => set({ accessToken: null, refreshToken: null, expiresAt: null, user: null }),
    }),
    { name: "forgevault-auth" },
  ),
);

export const authStore = useAuthStore;

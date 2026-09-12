import { create } from "zustand";
import { createJSONStorage, persist, type StateStorage } from "zustand/middleware";
import type { MeResponse } from "@/types/api";

const REMEMBER_ME_KEY = "forgevault-remember-me";

/** Whether the last login opted into surviving a closed browser — the "Stay logged in"
 * checkbox on LoginPage.tsx. Stored unconditionally in localStorage (it's a UI preference,
 * not anything sensitive) so the checkbox can default to the user's last choice.
 *
 * Unset (never written — e.g. every session before this checkbox existed) defaults to
 * `true`: before this, the persisted session always lived in localStorage unconditionally,
 * so defaulting the *unset* case to anything else would silently log out every
 * already-logged-in user the moment this ships. */
export function getRememberMe(): boolean {
  const stored = localStorage.getItem(REMEMBER_ME_KEY);
  return stored === null ? true : stored === "true";
}

export function setRememberMe(value: boolean): void {
  localStorage.setItem(REMEMBER_ME_KEY, value ? "true" : "false");
}

/** Routes the persisted auth state to localStorage (survives closing the browser) when
 * "Stay logged in" was checked, sessionStorage (cleared when the tab/browser closes)
 * otherwise. Checked on every read/write, not just at store creation, so a login's choice
 * takes effect immediately. removeItem always clears both, so logout never leaves a stale
 * copy behind in whichever storage a previous, differently-checked login used. */
const authStorage: StateStorage = {
  getItem: (name) => (getRememberMe() ? localStorage : sessionStorage).getItem(name),
  setItem: (name, value) => (getRememberMe() ? localStorage : sessionStorage).setItem(name, value),
  removeItem: (name) => {
    localStorage.removeItem(name);
    sessionStorage.removeItem(name);
  },
};

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
    { name: "forgevault-auth", storage: createJSONStorage(() => authStorage) },
  ),
);

export const authStore = useAuthStore;

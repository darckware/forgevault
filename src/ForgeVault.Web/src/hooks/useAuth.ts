import { useMutation, useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { useAuthStore } from "@/stores/authStore";
import type { LoginResponse, MeResponse } from "@/types/api";

export interface LoginPayload {
  email: string;
  password: string;
  mfaCode?: string;
  recaptchaToken?: string | null;
}

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession);
  const setUser = useAuthStore((s) => s.setUser);

  return useMutation({
    mutationFn: (payload: LoginPayload) =>
      apiFetch<LoginResponse>("/api/v1/auth/login", {
        method: "POST",
        body: JSON.stringify(payload),
        skipAuthRefresh: true,
      }),
    onSuccess: async (session) => {
      setSession(session);
      const me = await apiFetch<MeResponse>("/api/v1/auth/me");
      setUser(me);
    },
  });
}

export function useMe() {
  const accessToken = useAuthStore((s) => s.accessToken);
  return useQuery({
    queryKey: ["me"],
    queryFn: () => apiFetch<MeResponse>("/api/v1/auth/me"),
    enabled: !!accessToken,
  });
}

export function useLogout() {
  const clear = useAuthStore((s) => s.clear);
  return () => {
    clear();
    window.location.assign("/login");
  };
}

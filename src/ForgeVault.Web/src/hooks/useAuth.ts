import { useMutation, useQuery } from "@tanstack/react-query";
import { apiFetch } from "@/lib/api";
import { useAuthStore } from "@/stores/authStore";
import type {
  ChangePasswordResponse,
  LoginResponse,
  MeResponse,
  MfaEnrollResponse,
  MfaVerifyResponse,
} from "@/types/api";

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

export function useMfaEnroll() {
  return useMutation({
    mutationFn: () => apiFetch<MfaEnrollResponse>("/api/v1/auth/mfa/enroll", { method: "POST" }),
  });
}

export function useMfaVerify() {
  const setUser = useAuthStore((s) => s.setUser);
  const user = useAuthStore((s) => s.user);
  return useMutation({
    mutationFn: (code: string) => apiFetch<MfaVerifyResponse>("/api/v1/auth/mfa/verify", { method: "POST", body: JSON.stringify({ code }) }),
    onSuccess: () => {
      // The access token isn't reissued here, so mfaEnabled on it stays stale until the
      // next login/refresh — flip the cached profile locally so the account screen reflects
      // "MFA enabled" immediately instead of waiting for that.
      if (user) {
        setUser({ ...user, mfaEnabled: true });
      }
    },
  });
}

export function useChangeMyPassword() {
  return useMutation({
    mutationFn: (payload: { currentPassword: string; newPassword: string }) =>
      apiFetch<ChangePasswordResponse>("/api/v1/auth/change-password", { method: "POST", body: JSON.stringify(payload) }),
  });
}

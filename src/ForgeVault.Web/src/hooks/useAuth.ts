import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
  const qc = useQueryClient();
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
      qc.setQueryData(["me"], me);
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
  const qc = useQueryClient();
  const setUser = useAuthStore((s) => s.setUser);
  return useMutation({
    mutationFn: (code: string) => apiFetch<MfaVerifyResponse>("/api/v1/auth/mfa/verify", { method: "POST", body: JSON.stringify({ code }) }),
    onSuccess: () => {
      // The access token isn't reissued here, so mfaEnabled on it stays stale until the
      // next login/refresh. This used to only patch the zustand authStore's cached user —
      // AccountModal actually renders from useMe()'s separate react-query ["me"] cache, so
      // the "Ativo"/"Inativo" badge kept showing the old value until an unrelated refetch
      // (e.g. navigating away and back) happened to reload it. Patch both caches here so the
      // badge flips immediately, matching what the database already has.
      const updated = qc.setQueryData<MeResponse>(["me"], (current) => (current ? { ...current, mfaEnabled: true } : current));
      if (updated) {
        setUser(updated);
      }
    },
  });
}

export function useUpdateMe() {
  const qc = useQueryClient();
  const setUser = useAuthStore((s) => s.setUser);
  return useMutation({
    mutationFn: (payload: { firstName?: string | null; lastName?: string | null; avatarDataUrl?: string | null }) =>
      apiFetch<MeResponse>("/api/v1/auth/me", { method: "PUT", body: JSON.stringify(payload) }),
    onSuccess: (me) => {
      // Same two-cache issue as useMfaVerify — without this, a saved name/avatar only shows
      // up after something else happens to refetch ["me"].
      qc.setQueryData(["me"], me);
      setUser(me);
    },
  });
}

export function useMfaDisable() {
  const qc = useQueryClient();
  const setUser = useAuthStore((s) => s.setUser);
  return useMutation({
    mutationFn: (currentPassword: string) =>
      apiFetch<MfaVerifyResponse>("/api/v1/auth/mfa/disable", { method: "POST", body: JSON.stringify({ currentPassword }) }),
    onSuccess: () => {
      const updated = qc.setQueryData<MeResponse>(["me"], (current) => (current ? { ...current, mfaEnabled: false } : current));
      if (updated) {
        setUser(updated);
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

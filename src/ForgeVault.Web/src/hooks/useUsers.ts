import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPatch, apiPost } from "@/lib/api";
import type { UserResponse } from "@/types/api";

export function useUsers() {
  return useQuery({
    queryKey: ["users"],
    queryFn: () => apiGet<UserResponse[]>("/api/v1/users"),
  });
}

export function useCreateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { email: string; password: string; username?: string; firstName?: string; lastName?: string }) =>
      apiPost<UserResponse>("/api/v1/users", payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

export function useSetUserActive() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      apiPost<UserResponse>(`/api/v1/users/${id}/${active ? "reactivate" : "deactivate"}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

// Admin-side edit (Username/FirstName/LastName/IsAdmin) — distinct from useUpdateMe
// (useAuth.ts), which is self-service and deliberately can't touch Username or IsAdmin.
export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...payload }: { id: string; username?: string; firstName?: string; lastName?: string; isAdmin?: boolean }) =>
      apiPatch<UserResponse>(`/api/v1/users/${id}`, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

// Hard delete — distinct from useSetUserActive's deactivate, which is the reversible,
// audit-preserving choice for routine offboarding.
export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/api/v1/users/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }),
  });
}

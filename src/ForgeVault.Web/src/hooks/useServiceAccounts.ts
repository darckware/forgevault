import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api";
import type { ServiceAccountResponse, ServiceAccountTokenResponse } from "@/types/api";

export function useServiceAccounts() {
  return useQuery({
    queryKey: ["service-accounts"],
    queryFn: () => apiGet<ServiceAccountResponse[]>("/api/v1/service-accounts"),
  });
}

export function useServiceAccount(id: string | undefined) {
  const { data: accounts, ...rest } = useServiceAccounts();
  return { data: accounts?.find((a) => a.id === id), ...rest };
}

export function useCreateServiceAccount() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name: string }) => apiPost<ServiceAccountResponse>("/api/v1/service-accounts", payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["service-accounts"] }),
  });
}

// A useMutation, never cached — the raw token is shown exactly once by the backend and must
// never persist in any client-side store beyond the issuing component's own local state.
export function useIssueServiceAccountToken(serviceAccountId: string) {
  return useMutation({
    mutationFn: () => apiPost<ServiceAccountTokenResponse>(`/api/v1/service-accounts/${serviceAccountId}/tokens`),
  });
}

export function useRevokeServiceAccountToken(serviceAccountId: string) {
  return useMutation({
    mutationFn: (tokenId: string) =>
      apiPost<void>(`/api/v1/service-accounts/${serviceAccountId}/tokens/${tokenId}/revoke`),
  });
}

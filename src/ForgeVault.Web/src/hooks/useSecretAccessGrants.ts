import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api";
import type { SecretAccessGrantResponse } from "@/types/api";

export function useSecretAccessGrants(secretId: string | undefined) {
  return useQuery({
    queryKey: ["secret-access-grants", secretId],
    queryFn: () => apiGet<SecretAccessGrantResponse[]>(`/api/v1/secrets/${secretId}/access-grants`),
    enabled: !!secretId,
  });
}

export function useGrantSecretAccess(secretId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (identityId: string) =>
      apiPost<SecretAccessGrantResponse>(`/api/v1/secrets/${secretId}/access-grants`, { identityId }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["secret-access-grants", secretId] }),
  });
}

export function useRevokeSecretAccessGrant(secretId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (grantId: string) => apiPost<SecretAccessGrantResponse>(`/api/v1/secret-access-grants/${grantId}/revoke`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["secret-access-grants", secretId] }),
  });
}

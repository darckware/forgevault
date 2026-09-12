import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost, apiPut } from "@/lib/api";
import type { CredentialEnvelope, SecretResponse, SecretType, SecretVersionResponse } from "@/types/api";

export function useSecrets(environmentId: string | undefined) {
  return useQuery({
    queryKey: ["secrets", { environmentId }],
    queryFn: () => apiGet<SecretResponse[]>(`/api/v1/secrets?environmentId=${environmentId}`),
    enabled: !!environmentId,
  });
}

export function useSecret(id: string | undefined) {
  return useQuery({
    queryKey: ["secrets", id],
    queryFn: () => apiGet<SecretResponse>(`/api/v1/secrets/${id}`),
    enabled: !!id,
  });
}

export function useSecretVersions(id: string | undefined) {
  return useQuery({
    queryKey: ["secrets", id, "versions"],
    queryFn: () => apiGet<SecretVersionResponse[]>(`/api/v1/secrets/${id}/versions`),
    enabled: !!id,
  });
}

export function useCreateSecret(environmentId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: {
      name: string;
      type: SecretType;
      provider?: string;
      description?: string;
      value: string;
      expiresAt?: string;
    }) => apiPost<SecretResponse>("/api/v1/secrets", { environmentId, ...payload }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["secrets", { environmentId }] }),
  });
}

export function useUpdateSecret(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { value: string; description?: string; expiresAt?: string }) =>
      apiPut<SecretResponse>(`/api/v1/secrets/${id}`, payload),
    onSuccess: (secret) => {
      qc.invalidateQueries({ queryKey: ["secrets", id] });
      qc.invalidateQueries({ queryKey: ["secrets", id, "versions"] });
      qc.invalidateQueries({ queryKey: ["secrets", { environmentId: secret.environmentId }] });
    },
  });
}

export function useRotateSecret(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { value: string }) => apiPost<SecretResponse>(`/api/v1/secrets/${id}/rotate`, payload),
    onSuccess: (secret) => {
      qc.invalidateQueries({ queryKey: ["secrets", id] });
      qc.invalidateQueries({ queryKey: ["secrets", id, "versions"] });
      qc.invalidateQueries({ queryKey: ["secrets", { environmentId: secret.environmentId }] });
    },
  });
}

export function useRevokeSecret(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => apiPost<SecretResponse>(`/api/v1/secrets/${id}/revoke`),
    onSuccess: (secret) => {
      qc.invalidateQueries({ queryKey: ["secrets", id] });
      qc.invalidateQueries({ queryKey: ["secrets", { environmentId: secret.environmentId }] });
    },
  });
}

// Deliberately a useMutation, never a useQuery — the plaintext value must never enter the
// react-query cache. Calling it again always re-fetches (and re-audits) the value fresh.
export function useRevealSecret(id: string) {
  return useMutation({
    mutationFn: () => apiGet<CredentialEnvelope>(`/api/v1/secrets/${id}/value?mode=REVEAL`),
  });
}

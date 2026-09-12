import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { OrganizationResponse, OrganizationStatus } from "@/types/api";

export function useOrganizations() {
  return useQuery({
    queryKey: ["organizations"],
    queryFn: () => apiGet<OrganizationResponse[]>("/api/v1/organizations"),
  });
}

export function useOrganization(id: string | undefined) {
  return useQuery({
    queryKey: ["organizations", id],
    queryFn: () => apiGet<OrganizationResponse>(`/api/v1/organizations/${id}`),
    enabled: !!id,
  });
}

export function useCreateOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name: string; slug: string }) =>
      apiPost<OrganizationResponse>("/api/v1/organizations", payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["organizations"] }),
  });
}

export function useUpdateOrganization(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name?: string; slug?: string; status?: OrganizationStatus }) =>
      apiPut<OrganizationResponse>(`/api/v1/organizations/${id}`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["organizations"] });
      qc.invalidateQueries({ queryKey: ["organizations", id] });
    },
  });
}

export function useDeleteOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/api/v1/organizations/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["organizations"] }),
  });
}

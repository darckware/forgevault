import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPost } from "@/lib/api";
import type { EnvironmentKind, EnvironmentResponse } from "@/types/api";

export function useEnvironments(projectId: string | undefined) {
  return useQuery({
    queryKey: ["projects", projectId, "environments"],
    queryFn: () => apiGet<EnvironmentResponse[]>(`/api/v1/projects/${projectId}/environments`),
    enabled: !!projectId,
  });
}

export function useEnvironment(id: string | undefined) {
  return useQuery({
    queryKey: ["environments", id],
    queryFn: () => apiGet<EnvironmentResponse>(`/api/v1/environments/${id}`),
    enabled: !!id,
  });
}

export function useCreateEnvironment(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name: EnvironmentKind; slug: string }) =>
      apiPost<EnvironmentResponse>(`/api/v1/projects/${projectId}/environments`, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["projects", projectId, "environments"] }),
  });
}

export function useDeleteEnvironment(projectId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/api/v1/environments/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["projects", projectId, "environments"] }),
  });
}

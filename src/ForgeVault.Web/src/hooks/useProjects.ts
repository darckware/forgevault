import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPost, apiPut } from "@/lib/api";
import type { ProjectResponse, ProjectStatus } from "@/types/api";

export function useProjects(organizationId: string | undefined) {
  return useQuery({
    queryKey: ["organizations", organizationId, "projects"],
    queryFn: () => apiGet<ProjectResponse[]>(`/api/v1/organizations/${organizationId}/projects`),
    enabled: !!organizationId,
  });
}

export function useProject(id: string | undefined) {
  return useQuery({
    queryKey: ["projects", id],
    queryFn: () => apiGet<ProjectResponse>(`/api/v1/projects/${id}`),
    enabled: !!id,
  });
}

export function useCreateProject(organizationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name: string; slug: string; description?: string }) =>
      apiPost<ProjectResponse>(`/api/v1/organizations/${organizationId}/projects`, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["organizations", organizationId, "projects"] }),
  });
}

export function useUpdateProject(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { name?: string; slug?: string; description?: string; status?: ProjectStatus }) =>
      apiPut<ProjectResponse>(`/api/v1/projects/${id}`, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["projects", id] }),
  });
}

export function useDeleteProject(organizationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/api/v1/projects/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["organizations", organizationId, "projects"] }),
  });
}

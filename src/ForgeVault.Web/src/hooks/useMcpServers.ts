import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiDelete, apiGet, apiPost } from "@/lib/api";
import type { McpServerDefinitionResponse, McpTransportType } from "@/types/api";

export function useMcpServerDefinitions(organizationId: string | undefined) {
  return useQuery({
    queryKey: ["mcp-servers", { organizationId }],
    queryFn: () => apiGet<McpServerDefinitionResponse[]>(`/api/v1/organizations/${organizationId}/mcp-servers`),
    enabled: !!organizationId,
  });
}

export function useMcpServerDefinition(id: string | undefined) {
  return useQuery({
    queryKey: ["mcp-servers", id],
    queryFn: () => apiGet<McpServerDefinitionResponse>(`/api/v1/mcp-servers/${id}`),
    enabled: !!id,
  });
}

export function useCreateMcpServerDefinition(organizationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: {
      name: string;
      transport: McpTransportType;
      command?: string;
      args?: string[];
      url?: string;
      timeout?: number;
      connectTimeout?: number;
      staticEnv?: Record<string, string>;
      secretParamNames?: string[];
    }) => apiPost<McpServerDefinitionResponse>(`/api/v1/organizations/${organizationId}/mcp-servers`, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["mcp-servers", { organizationId }] }),
  });
}

export function useDeleteMcpServerDefinition(organizationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiDelete<void>(`/api/v1/mcp-servers/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["mcp-servers", { organizationId }] }),
  });
}

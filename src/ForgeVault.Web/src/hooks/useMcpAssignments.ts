import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api";
import type { McpParamValue, McpServerAssignmentResponse, McpServerRenderResponse } from "@/types/api";

export function useMcpAssignmentsForIdentity(identityId: string | undefined) {
  return useQuery({
    queryKey: ["mcp-assignments", identityId],
    queryFn: () => apiGet<McpServerAssignmentResponse[]>(`/api/v1/identities/${identityId}/mcp-assignments`),
    enabled: !!identityId,
  });
}

export function useGrantMcpAssignment() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { identityId: string; mcpServerDefinitionId: string; paramValues: Record<string, McpParamValue> }) =>
      apiPost<McpServerAssignmentResponse>(`/api/v1/identities/${payload.identityId}/mcp-assignments`, {
        mcpServerDefinitionId: payload.mcpServerDefinitionId,
        paramValues: payload.paramValues,
      }),
    onSuccess: (assignment) => qc.invalidateQueries({ queryKey: ["mcp-assignments", assignment.identityId] }),
  });
}

export function useRevokeMcpAssignment(identityId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (assignmentId: string) => apiPost<McpServerAssignmentResponse>(`/api/v1/mcp-assignments/${assignmentId}/revoke`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["mcp-assignments", identityId] }),
  });
}

// Deliberately a useMutation, never a useQuery — the render resolves and decrypts every
// referenced Secret, same sensitivity as useRevealSecret, so the result must never enter the
// react-query cache. Calling it again always re-fetches (and re-audits) fresh.
export function useRenderMcpAssignment(assignmentId: string) {
  return useMutation({
    mutationFn: () => apiGet<McpServerRenderResponse>(`/api/v1/mcp-assignments/${assignmentId}/render`),
  });
}

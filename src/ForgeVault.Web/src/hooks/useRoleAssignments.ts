import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiGet, apiPost } from "@/lib/api";
import type { Role, RoleAssignmentResponse, RoleScopeType } from "@/types/api";

export function useRoleAssignmentsForIdentity(identityId: string | undefined) {
  return useQuery({
    queryKey: ["role-assignments", identityId],
    queryFn: () => apiGet<RoleAssignmentResponse[]>(`/api/v1/identities/${identityId}/role-assignments`),
    enabled: !!identityId,
  });
}

export function useGrantRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: { identityId: string; role: Role; scopeType: RoleScopeType; scopeId: string }) =>
      apiPost<RoleAssignmentResponse>(`/api/v1/identities/${payload.identityId}/role-assignments`, {
        role: payload.role,
        scopeType: payload.scopeType,
        scopeId: payload.scopeId,
      }),
    onSuccess: (assignment) => qc.invalidateQueries({ queryKey: ["role-assignments", assignment.identityId] }),
  });
}

export function useRevokeRoleAssignment(identityId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (assignmentId: string) => apiPost<RoleAssignmentResponse>(`/api/v1/role-assignments/${assignmentId}/revoke`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["role-assignments", identityId] }),
  });
}

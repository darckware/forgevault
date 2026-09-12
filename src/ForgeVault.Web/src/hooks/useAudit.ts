import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api";
import type { AuditLogPage } from "@/types/api";

export interface AuditFilters {
  actorId?: string;
  resourceType?: string;
  resourceId?: string;
  action?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export function useAudit(filters: AuditFilters, options?: { refetchInterval?: number }) {
  const params = new URLSearchParams();
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== "") {
      params.set(key, String(value));
    }
  });

  return useQuery({
    queryKey: ["audit", filters],
    queryFn: () => apiGet<AuditLogPage>(`/api/v1/audit?${params.toString()}`),
    refetchInterval: options?.refetchInterval,
  });
}

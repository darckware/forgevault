import { useQueries, useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api";
import type { AuditLogPage, ServiceAccountResponse } from "@/types/api";

export interface AgentActivity {
  serviceAccount: ServiceAccountResponse;
  lastAction: string | null;
  lastSeenAt: string | null;
}

const ACTIVE_WINDOW_MS = 15 * 60 * 1000; // matches the access token lifetime — a reasonable
// "recently active" bar without ForgeVault ever having to know about Hermes gateway state.

export function isRecentlyActive(lastSeenAt: string | null): boolean {
  if (!lastSeenAt) return false;
  return Date.now() - new Date(lastSeenAt).getTime() <= ACTIVE_WINDOW_MS;
}

// Derives "is this agent alive" from its OWN audit trail (its most recent action against
// ForgeVault) rather than shelling out to check Hermes gateway processes — keeps ForgeVault's
// blast radius to itself (a secrets manager should never need host-exec permissions), while
// still giving a genuinely meaningful signal: this identity used its token this recently.
export function useAgentActivity() {
  const { data: serviceAccounts, isLoading: accountsLoading } = useQuery({
    queryKey: ["service-accounts"],
    queryFn: () => apiGet<ServiceAccountResponse[]>("/api/v1/service-accounts"),
  });

  const activityQueries = useQueries({
    queries: (serviceAccounts ?? []).map((sa) => ({
      queryKey: ["audit", { actorId: sa.id, pageSize: 1 }],
      queryFn: () => apiGet<AuditLogPage>(`/api/v1/audit?actorId=${sa.id}&pageSize=1`),
      enabled: !!serviceAccounts,
      refetchInterval: 30_000,
    })),
  });

  const activities: AgentActivity[] = (serviceAccounts ?? []).map((sa, i) => {
    const page = activityQueries[i]?.data;
    const latest = page?.items?.[0];
    return {
      serviceAccount: sa,
      lastAction: latest?.action ?? null,
      lastSeenAt: latest?.timestamp ?? null,
    };
  });

  activities.sort((a, b) => (b.lastSeenAt ?? "").localeCompare(a.lastSeenAt ?? ""));

  return {
    activities,
    isLoading: accountsLoading || activityQueries.some((q) => q.isLoading),
  };
}

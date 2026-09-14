import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api";
import type { SystemVersionResponse } from "@/types/api";

export function useSystemVersion(enabled: boolean) {
  return useQuery({
    queryKey: ["system-version"],
    queryFn: () => apiGet<SystemVersionResponse>("/api/v1/system/version"),
    enabled,
  });
}

import { useQuery } from "@tanstack/react-query";
import { apiGet } from "@/lib/api";
import type { EnvironmentResponse, OrganizationResponse, ProjectResponse, SecretResponse } from "@/types/api";

export interface HealthStatus {
  status: string;
  dependencies?: { postgres: string };
}

export function useHealthLive() {
  return useQuery({
    queryKey: ["health", "live"],
    queryFn: () => apiGet<HealthStatus>("/health/live"),
    refetchInterval: 15_000,
    retry: false,
  });
}

export function useHealthReady() {
  return useQuery({
    queryKey: ["health", "ready"],
    queryFn: () => apiGet<HealthStatus>("/health/ready"),
    refetchInterval: 15_000,
    retry: false,
  });
}

export interface SystemStats {
  organizationCount: number;
  projectCount: number;
  environmentCount: number;
  secretCount: number;
  activeSecretCount: number;
  expiringSoon: SecretResponse[];
}

const EXPIRING_SOON_WINDOW_MS = 7 * 24 * 60 * 60 * 1000; // 7 days

// Fans out across the whole visible Organization -> Project -> Environment -> Secret tree
// to compute totals. Fine at today's scale (a handful of orgs/projects); if this ever grows
// large, add a dedicated aggregate endpoint instead of fanning out client-side.
export function useSystemStats() {
  return useQuery({
    queryKey: ["system-stats"],
    queryFn: async (): Promise<SystemStats> => {
      const organizations = await apiGet<OrganizationResponse[]>("/api/v1/organizations");

      const projectsByOrg = await Promise.all(
        organizations.map((org) => apiGet<ProjectResponse[]>(`/api/v1/organizations/${org.id}/projects`)),
      );
      const projects = projectsByOrg.flat();

      const environmentsByProject = await Promise.all(
        projects.map((p) => apiGet<EnvironmentResponse[]>(`/api/v1/projects/${p.id}/environments`)),
      );
      const environments = environmentsByProject.flat();

      const secretsByEnv = await Promise.all(
        environments.map((e) => apiGet<SecretResponse[]>(`/api/v1/secrets?environmentId=${e.id}`)),
      );
      const secrets = secretsByEnv.flat();

      const now = Date.now();
      const expiringSoon = secrets.filter((s) => {
        if (!s.expiresAt) return false;
        const expiresAt = new Date(s.expiresAt).getTime();
        return expiresAt > now && expiresAt - now <= EXPIRING_SOON_WINDOW_MS;
      });

      return {
        organizationCount: organizations.length,
        projectCount: projects.length,
        environmentCount: environments.length,
        secretCount: secrets.length,
        activeSecretCount: secrets.filter((s) => s.status === "Active").length,
        expiringSoon,
      };
    },
    refetchInterval: 30_000,
  });
}

import { authStore } from "@/stores/authStore";
import type { LoginResponse } from "@/types/api";

// Empty base = same-origin relative "/api/..." calls, matching the nginx reverse-proxy in
// production and the Vite dev-server proxy in development — never a hardcoded backend host,
// so the browser never needs to know the API's real address (docker-compose.yml keeps the
// api container off any public-facing binding).
const API_URL = import.meta.env.VITE_API_URL ?? "";

export class ApiError extends Error {
  status: number;
  body?: { error?: string } & Record<string, unknown>;

  constructor(message: string, status: number, body?: Record<string, unknown>) {
    super(message);
    this.status = status;
    this.body = body;
  }
}

// Single-flight refresh: if a 401 arrives while a refresh is already in progress, every
// caller joins the SAME promise instead of firing a second /refresh call. Cleared only after
// the in-flight refresh settles (success or failure), so late joiners still await it.
let refreshPromise: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  if (refreshPromise) {
    return refreshPromise;
  }

  refreshPromise = (async () => {
    const currentRefreshToken = authStore.getState().refreshToken;
    if (!currentRefreshToken) {
      return null;
    }

    try {
      const res = await fetch(`${API_URL}/api/v1/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: currentRefreshToken }),
      });
      if (!res.ok) {
        return null;
      }

      const session = (await res.json()) as LoginResponse;
      // Refresh tokens rotate server-side (reuse detection) — always persist the new one.
      authStore.getState().setSession(session);
      return session.accessToken;
    } catch {
      return null;
    }
  })();

  try {
    return await refreshPromise;
  } finally {
    refreshPromise = null;
  }
}

interface ApiFetchOptions extends RequestInit {
  skipAuthRefresh?: boolean;
}

export async function apiFetch<T>(path: string, init: ApiFetchOptions = {}, _retried = false): Promise<T> {
  const { skipAuthRefresh, ...requestInit } = init;
  const token = authStore.getState().accessToken;

  const res = await fetch(`${API_URL}${path}`, {
    ...requestInit,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...requestInit.headers,
    },
  });

  if (res.status === 401 && !_retried && !skipAuthRefresh) {
    const newToken = await refreshAccessToken();
    if (newToken) {
      return apiFetch<T>(path, init, true);
    }

    authStore.getState().clear();
    window.location.assign("/login");
    throw new ApiError("unauthenticated", 401);
  }

  if (!res.ok) {
    const body = await res.json().catch(() => ({}) as Record<string, unknown>);
    throw new ApiError((body as { error?: string }).error ?? res.statusText, res.status, body);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return (await res.json()) as T;
}

export const apiGet = <T>(path: string) => apiFetch<T>(path, { method: "GET" });

export const apiPost = <T>(path: string, body?: unknown) =>
  apiFetch<T>(path, { method: "POST", body: body !== undefined ? JSON.stringify(body) : undefined });

export const apiPut = <T>(path: string, body?: unknown) =>
  apiFetch<T>(path, { method: "PUT", body: body !== undefined ? JSON.stringify(body) : undefined });

export const apiDelete = <T>(path: string) => apiFetch<T>(path, { method: "DELETE" });

import type { EnvironmentKind, OrganizationStatus, ProjectStatus, SecretStatus } from "@/types/api";

export type BadgeTone = "neutral" | "success" | "warning" | "danger" | "info";

const DANGER_STATUSES = new Set(["Expired", "Revoked", "Archived", "Suspended"]);
const WARNING_STATUSES = new Set(["Rotating"]);

// Single deterministic teal/amber/rose mapping reused by every status badge in the app —
// Active (and anything not explicitly warning/danger) reads as "sealed and healthy" (teal),
// Suspended/Rotating as "in flux" (amber), Expired/Revoked/Archived as "sealed off" (rose).
export function statusTone(status: SecretStatus | OrganizationStatus | ProjectStatus | string): BadgeTone {
  if (DANGER_STATUSES.has(status)) {
    return "danger";
  }
  if (WARNING_STATUSES.has(status)) {
    return "warning";
  }
  if (status === "Active") {
    return "success";
  }
  return "neutral";
}

export function environmentKindTone(kind: EnvironmentKind): BadgeTone {
  switch (kind) {
    case "Production":
      return "danger";
    case "Staging":
      return "warning";
    case "Shared":
      return "info";
    default:
      return "neutral";
  }
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }
  return new Date(value).toLocaleString();
}

export function truncateId(id: string): string {
  if (id.length <= 13) {
    return id;
  }
  return `${id.slice(0, 8)}…${id.slice(-4)}`;
}

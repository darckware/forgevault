// Mirrors ForgeVault.Api's *Response/*Request records exactly (src/ForgeVault.Api/Endpoints/*.cs).
// Enums serialize as PascalCase strings (global JsonStringEnumConverter) — string-literal
// unions here, not TS enums, so the values line up 1:1 with the wire format.

export type OrganizationStatus = "Active" | "Suspended";
export type ProjectStatus = "Active" | "Suspended";
export type EnvironmentKind = "Development" | "Staging" | "Production" | "Shared";

export const SECRET_TYPES = [
  "Password",
  "ApiKey",
  "AccessToken",
  "RefreshToken",
  "LlmToken",
  "SshPrivateKey",
  "SshPassword",
  "DatabaseCredential",
  "OAuthClient",
  "Certificate",
  "PrivateKey",
  "ServiceAccount",
  "WebhookSecret",
  "EnvSecret",
  "TotpSeed",
  "SystemCredential",
  "GenericSecret",
] as const;
export type SecretType = (typeof SECRET_TYPES)[number];

export type SecretStatus = "Active" | "Suspended" | "Rotating" | "Expired" | "Revoked" | "Archived";

export const ROLES = [
  "Owner",
  "Admin",
  "SecurityAdmin",
  "ProjectAdmin",
  "Developer",
  "Operator",
  "Auditor",
  "ReadOnly",
  "Agent",
  "ServiceAccount",
] as const;
export type Role = (typeof ROLES)[number];

export const ROLE_SCOPE_TYPES = ["Organization", "Project", "Environment"] as const;
export type RoleScopeType = (typeof ROLE_SCOPE_TYPES)[number];

export interface ErrorResponse {
  error: string;
}

// --- Auth ---
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

export interface MeResponse {
  id: string;
  email: string;
}

export interface MfaEnrollResponse {
  base32Secret: string;
  otpAuthUri: string;
}

// --- Organizations ---
export interface OrganizationResponse {
  id: string;
  name: string;
  slug: string;
  status: OrganizationStatus;
  createdAt: string;
  updatedAt: string;
}

// --- Projects ---
export interface ProjectResponse {
  id: string;
  organizationId: string;
  name: string;
  slug: string;
  description: string | null;
  status: ProjectStatus;
  createdAt: string;
  updatedAt: string;
}

// --- Environments ---
export interface EnvironmentResponse {
  id: string;
  projectId: string;
  name: EnvironmentKind;
  slug: string;
  createdAt: string;
}

// --- Secrets ---
export interface SecretResponse {
  id: string;
  environmentId: string;
  name: string;
  type: SecretType;
  provider: string | null;
  description: string | null;
  status: SecretStatus;
  currentVersion: number;
  createdAt: string;
  updatedAt: string;
  expiresAt: string | null;
}

export interface SecretVersionResponse {
  id: string;
  version: number;
  algorithm: string;
  createdBy: string;
  createdAt: string;
}

export interface CredentialEnvelope {
  requestId: string;
  identity: string;
  resource: string;
  accessMode: string;
  expiresAt: string | null;
  credentials: { value: string } | null;
  session: unknown;
  broker: unknown;
  lease: unknown;
  metadata: unknown;
}

// --- Service Accounts ---
export interface ServiceAccountResponse {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
}

export interface ServiceAccountTokenResponse {
  token: string;
  tokenPrefix: string;
  issuedAt: string;
}

// --- Audit ---
export interface AuditLogResponse {
  id: string;
  actorId: string;
  actorType: string;
  action: string;
  resourceType: string;
  resourceId: string;
  requestId: string;
  correlationId: string | null;
  timestamp: string;
  metadata: string;
}

export interface AuditLogPage {
  items: AuditLogResponse[];
  page: number;
  pageSize: number;
  total: number;
}

// --- Role Assignments (M9) ---
export interface RoleAssignmentResponse {
  id: string;
  identityId: string;
  role: Role;
  scopeType: RoleScopeType;
  scopeId: string;
  status: "active" | "revoked";
  createdAt: string;
  revokedAt: string | null;
}

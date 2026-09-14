import type { SecretType } from "@/types/api";

// The API/domain model (Secret.Type, docs/modules/03_SECRETS_AND_ENCRYPTION.md §4) already
// classifies a secret as a site login, a database credential, a provider token, etc. — but
// SecretVersion.Value has always been a single opaque string. Rather than changing storage,
// a handful of types get a small structured JSON shape encoded into that same string field,
// so the UI can offer real fields (URL/user/password, host/port/db/user/password) instead of
// one blank text box no matter what you're registering. Anything not listed here keeps the
// plain single-value form it always had.

export interface SiteLoginFields {
  url: string;
  username: string;
  password: string;
}

export interface DatabaseCredentialFields {
  host: string;
  port: string;
  database: string;
  username: string;
  password: string;
}

export type StructuredSecretType = Extract<SecretType, "Password" | "DatabaseCredential">;

export function isStructuredSecretType(type: SecretType): type is StructuredSecretType {
  return type === "Password" || type === "DatabaseCredential";
}

// `type` isn't used to shape the encoding (any string-keyed record round-trips through
// JSON the same way) — it's still a required param so call sites read as self-documenting
// ("this is a Password" / "this is a DatabaseCredential"), not because encode/decode branch
// on it today.
export function encodeStructuredValue(_type: StructuredSecretType, fields: Record<string, string>): string {
  return JSON.stringify(fields);
}

// Best-effort decode for display: legacy/plain values (or a value typed directly, not
// through the structured form) just aren't JSON — treated as "not structured" rather than
// an error.
export function decodeStructuredValue(_type: StructuredSecretType, raw: string): Record<string, string> | null {
  try {
    const parsed: unknown = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as Record<string, string>;
    }
    return null;
  } catch {
    return null;
  }
}

// Human-readable labels for the Type dropdown (SecretForm) — the raw enum names
// (esp. "Password" and "DatabaseCredential") don't read as "site login credential" or
// "database credential" on their own, which made the structured form above easy to miss.
export const SECRET_TYPE_LABELS: Record<SecretType, string> = {
  Password: "Password (site login)",
  ApiKey: "API Key",
  AccessToken: "Access Token",
  RefreshToken: "Refresh Token",
  LlmToken: "LLM Token",
  SshPrivateKey: "SSH Private Key",
  SshPassword: "SSH Password",
  DatabaseCredential: "Database Credential",
  OAuthClient: "OAuth Client",
  Certificate: "Certificate",
  PrivateKey: "Private Key",
  ServiceAccount: "Service Account Credential",
  WebhookSecret: "Webhook Secret",
  EnvSecret: "Env Secret",
  TotpSeed: "TOTP Seed",
  SystemCredential: "System Credential",
  GenericSecret: "Generic Secret",
};

export const STRUCTURED_FIELD_DEFS: Record<StructuredSecretType, { key: keyof SiteLoginFields | keyof DatabaseCredentialFields; label: string; placeholder?: string; sensitive?: boolean }[]> = {
  Password: [
    { key: "url", label: "Site URL", placeholder: "https://example.com/login" },
    { key: "username", label: "Username" },
    { key: "password", label: "Password", sensitive: true },
  ],
  DatabaseCredential: [
    { key: "host", label: "Host", placeholder: "db.internal" },
    { key: "port", label: "Port", placeholder: "5432" },
    { key: "database", label: "Database name" },
    { key: "username", label: "Username" },
    { key: "password", label: "Password", sensitive: true },
  ],
};

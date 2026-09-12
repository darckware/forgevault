#!/usr/bin/env python3
"""
Imports credentials from a .env file into ForgeVault as Secrets.

Problem this solves: real credentials (database passwords, SSH keys, API tokens) tend to
live scattered across .env files on disk instead of in the vault. This script reads one
.env file, groups related keys by service (e.g. DB_HOST/DB_PASSWORD/DB_URL all belong to
"DB"), classifies each group's service type (provider) and pulls out its access IP/host and
link/URL into the secret's description, then registers the actual credential(s) as Secrets
in a chosen ForgeVault Environment.

Non-secret-looking keys (a bare host/port/URL with no accompanying password/token/key in
the same group) are never created as their own Secret -- they only enrich the description
of a real credential found in the same group. A group with no credential at all is skipped
entirely (nothing sensitive to store).

Usage:
    python3 import_env.py --api-url http://localhost:8080 \\
        --email admin@darckware.local --environment-id <guid> \\
        --file /path/to/.env [--dry-run]

Password is read from the FORGEVAULT_PASSWORD env var if set, otherwise prompted
interactively (never pass it as a CLI argument -- it would land in shell history).

Alternatively, skip login entirely with --token fv_sa_... (a Service Account token) or by
setting FORGEVAULT_TOKEN.
"""
import argparse
import getpass
import json
import os
import re
import sys
import urllib.error
import urllib.request
from dataclasses import dataclass, field

CREDENTIAL_SUFFIXES = ["PRIVATE_KEY", "APIKEY", "API_KEY", "PASSWORD", "SECRET", "TOKEN", "PASS", "PWD", "KEY"]
HOST_SUFFIXES = ["HOSTNAME", "ADDRESS", "HOST", "ADDR", "IP"]
LINK_SUFFIXES = ["ENDPOINT", "URL", "URI", "LINK"]
PORT_SUFFIXES = ["PORT"]
USER_SUFFIXES = ["USERNAME", "LOGIN", "USER"]

# Prefix/keyword -> (provider label, is_database). Checked against the group prefix and the
# full key, case-insensitively. Order matters: more specific entries first.
SERVICE_TYPE_MAP = [
    ("POSTGRES", "postgres", True),
    ("POSTGRESQL", "postgres", True),
    ("PG", "postgres", True),
    ("MYSQL", "mysql", True),
    ("MARIADB", "mysql", True),
    ("MONGO", "mongodb", True),
    ("REDIS", "redis", True),
    ("RABBITMQ", "rabbitmq", False),
    ("AMQP", "rabbitmq", False),
    ("SSH", "ssh", False),
    ("SFTP", "sftp", False),
    ("FTP", "ftp", False),
    ("SMTP", "smtp", False),
    ("MAIL", "smtp", False),
    ("AWS", "aws", False),
    ("S3", "aws-s3", False),
    ("GCP", "gcp", False),
    ("AZURE", "azure", False),
    ("SLACK", "slack", False),
    ("GITHUB", "github", False),
    ("OPENAI", "openai", False),
    ("STRIPE", "stripe", False),
    ("API", "api", False),
    # Generic fallback -- a bare "DB"/"DATABASE" prefix (no specific engine named) still
    # means "this is a database credential", just with an unknown engine.
    ("DATABASE", "database", True),
    ("DB", "database", True),
]

# Connection-string scheme -> (provider label, is_database). Used to refine a "generic"
# classification when the group's Link value gives away the real engine (e.g. DB_URL=
# postgres://... even though the key itself is just "DB_*", not "POSTGRES_*").
URL_SCHEME_MAP = {
    "postgres": ("postgres", True),
    "postgresql": ("postgres", True),
    "mysql": ("mysql", True),
    "mariadb": ("mysql", True),
    "mongodb": ("mongodb", True),
    "mongodb+srv": ("mongodb", True),
    "redis": ("redis", True),
    "rediss": ("redis", True),
    "amqp": ("rabbitmq", False),
    "amqps": ("rabbitmq", False),
    "ftp": ("ftp", False),
    "sftp": ("sftp", False),
}


def classify_from_link(link: str | None) -> tuple[str, bool] | None:
    if not link or "://" not in link:
        return None
    scheme = link.split("://", 1)[0].lower()
    return URL_SCHEME_MAP.get(scheme)


@dataclass
class SecretCandidate:
    name: str
    value: str
    secret_type: str
    provider: str
    description: str


def parse_env_file(path: str) -> dict[str, str]:
    values: dict[str, str] = {}
    with open(path, encoding="utf-8") as f:
        for raw_line in f:
            line = raw_line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            if line.startswith("export "):
                line = line[len("export ") :]
            key, _, value = line.partition("=")
            key = key.strip()
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            if key:
                values[key] = value
    return values


def strip_suffix(key: str, suffixes: list[str]) -> tuple[str, bool]:
    upper = key.upper()
    for suffix in suffixes:
        if upper == suffix or upper.endswith("_" + suffix):
            prefix = key[: len(key) - len(suffix)].rstrip("_")
            return prefix, True
    return key, False


def classify_service(prefix: str, full_key: str) -> tuple[str, bool]:
    haystack = f"{prefix}_{full_key}".upper()
    for keyword, provider, is_db in SERVICE_TYPE_MAP:
        if keyword in haystack:
            return provider, is_db
    return "generic", False


def infer_secret_type(credential_key: str, is_database: bool) -> str:
    upper = credential_key.upper()
    if "PRIVATE_KEY" in upper or ("SSH" in upper and "KEY" in upper):
        return "SshPrivateKey"
    if "SSH" in upper and ("PASS" in upper or "PWD" in upper):
        return "SshPassword"
    if is_database and ("PASS" in upper or "PWD" in upper or "SECRET" in upper):
        return "DatabaseCredential"
    if "TOKEN" in upper:
        return "AccessToken"
    if "APIKEY" in upper or "API_KEY" in upper or upper.endswith("KEY"):
        return "ApiKey"
    if "PASS" in upper or "PWD" in upper:
        return "Password"
    if "WEBHOOK" in upper:
        return "WebhookSecret"
    if "CERT" in upper:
        return "Certificate"
    return "GenericSecret"


def group_and_classify(values: dict[str, str]) -> list[SecretCandidate]:
    groups: dict[str, dict[str, str]] = {}
    for key, value in values.items():
        for suffixes in (CREDENTIAL_SUFFIXES, HOST_SUFFIXES, LINK_SUFFIXES, PORT_SUFFIXES, USER_SUFFIXES):
            prefix, matched = strip_suffix(key, suffixes)
            if matched:
                groups.setdefault(prefix or "_", {})[key] = value
                break
        else:
            # No recognized suffix at all -- treat the whole key as its own group so an
            # unclassified credential-looking key (e.g. just "SECRET") is still considered.
            groups.setdefault(key, {})[key] = value

    candidates: list[SecretCandidate] = []
    for prefix, group in groups.items():
        credential_key = None
        for key in group:
            _, is_cred = strip_suffix(key, CREDENTIAL_SUFFIXES)
            if is_cred:
                credential_key = key
                break
        if credential_key is None:
            continue  # nothing sensitive in this group -- e.g. just a HOST/PORT pair

        host = next((v for k, v in group.items() if strip_suffix(k, HOST_SUFFIXES)[1]), None)
        link = next((v for k, v in group.items() if strip_suffix(k, LINK_SUFFIXES)[1]), None)
        port = next((v for k, v in group.items() if strip_suffix(k, PORT_SUFFIXES)[1]), None)
        user = next((v for k, v in group.items() if strip_suffix(k, USER_SUFFIXES)[1]), None)

        provider, is_db = classify_service(prefix, credential_key)
        if provider in ("generic", "database"):
            # The key naming alone didn't name a specific engine (e.g. "DB_PASSWORD") --
            # the connection string in the same group often gives it away (postgres://...).
            refined = classify_from_link(link)
            if refined:
                provider, is_db = refined
        secret_type = infer_secret_type(credential_key, is_db)

        description_parts = []
        if host:
            description_parts.append(f"IP/Host: {host}" + (f":{port}" if port else ""))
        if link:
            description_parts.append(f"Link: {link}")
        if user:
            description_parts.append(f"Usuário: {user}")
        description_parts.append(f"Importado de .env (grupo: {prefix or credential_key})")

        candidates.append(
            SecretCandidate(
                name=credential_key,
                value=group[credential_key],
                secret_type=secret_type,
                provider=provider,
                description=" | ".join(description_parts),
            )
        )
    return candidates


class ForgeVaultClient:
    def __init__(self, api_url: str, token: str):
        self.api_url = api_url.rstrip("/")
        self.token = token

    def _request(self, method: str, path: str, body: dict | None = None) -> tuple[int, dict]:
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(f"{self.api_url}{path}", data=data, method=method)
        req.add_header("Authorization", f"Bearer {self.token}")
        req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req) as resp:
                raw = resp.read()
                return resp.status, (json.loads(raw) if raw else {})
        except urllib.error.HTTPError as e:
            raw = e.read()
            try:
                return e.code, json.loads(raw)
            except json.JSONDecodeError:
                return e.code, {"error": raw.decode(errors="replace")}

    @classmethod
    def login(cls, api_url: str, email: str, password: str) -> "ForgeVaultClient":
        client = cls(api_url, token="")
        status, body = client._request("POST", "/api/v1/auth/login", {"email": email, "password": password})
        if status != 200:
            raise SystemExit(f"Login failed ({status}): {body.get('error', body)}")
        client.token = body["accessToken"]
        return client

    def create_secret(self, environment_id: str, candidate: SecretCandidate) -> tuple[bool, str]:
        status, body = self._request(
            "POST",
            "/api/v1/secrets",
            {
                "environmentId": environment_id,
                "name": candidate.name,
                "type": candidate.secret_type,
                "provider": candidate.provider,
                "description": candidate.description,
                "value": candidate.value,
                "expiresAt": None,
            },
        )
        if status == 201:
            return True, "created"
        if status == 409:
            return False, f"skipped ({body.get('error', 'conflict')}) -- a secret with this name already exists"
        return False, f"failed ({status}): {body.get('error', body)}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--file", required=True, help="Path to the .env file to import")
    parser.add_argument("--api-url", default=os.environ.get("FORGEVAULT_API_URL", "http://localhost:8080"))
    parser.add_argument("--environment-id", required=True, help="Target Environment GUID")
    parser.add_argument("--email", help="ForgeVault human login email (or set FORGEVAULT_TOKEN instead)")
    parser.add_argument("--token", default=os.environ.get("FORGEVAULT_TOKEN"), help="Bearer token (skips login)")
    parser.add_argument("--dry-run", action="store_true", help="Show what would be imported without calling the API")
    args = parser.parse_args()

    values = parse_env_file(args.file)
    candidates = group_and_classify(values)

    if not candidates:
        print("No credential-looking keys found in this file.")
        return

    print(f"Found {len(candidates)} credential(s) to import from {args.file}:\n")
    for c in candidates:
        masked = c.value[:3] + "***" if len(c.value) > 3 else "***"
        print(f"  {c.name:30} type={c.secret_type:20} provider={c.provider:10} value={masked}")
        print(f"    {c.description}")

    if args.dry_run:
        print("\n--dry-run: nothing was sent to ForgeVault.")
        return

    if args.token:
        client = ForgeVaultClient(args.api_url, args.token)
    else:
        if not args.email:
            raise SystemExit("Provide --email (for interactive login) or --token/FORGEVAULT_TOKEN.")
        password = os.environ.get("FORGEVAULT_PASSWORD") or getpass.getpass("ForgeVault password: ")
        client = ForgeVaultClient.login(args.api_url, args.email, password)

    print()
    ok_count = 0
    for c in candidates:
        ok, message = client.create_secret(args.environment_id, c)
        status_icon = "✓" if ok else "✗"
        print(f"  {status_icon} {c.name}: {message}")
        ok_count += 1 if ok else 0

    print(f"\n{ok_count}/{len(candidates)} secrets created.")


if __name__ == "__main__":
    main()

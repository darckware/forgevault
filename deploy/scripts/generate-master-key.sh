#!/usr/bin/env bash
# Generates the ForgeVault Master Key file for local/MVP use.
#
# docs/ForgeVault.md §13, §141: the Master Key is never generated silently by the
# application at runtime — it must exist, with correct permissions, before ForgeVault
# starts (LocalFileKeyProvider fails fast otherwise). Production/mature deployments
# replace this with a real KMS/HSM provider (docs/ForgeVault.md §143, Fase 4).
set -euo pipefail

KEY_PATH="${FORGEVAULT_MASTER_KEY_PATH:-/root/.forgevault/master.key}"
KEY_DIR="$(dirname "$KEY_PATH")"

if [ -e "$KEY_PATH" ]; then
  echo "Master key already exists at $KEY_PATH — refusing to overwrite it." >&2
  echo "Delete it manually first if you really intend to replace it (this invalidates every secret encrypted under it)." >&2
  exit 1
fi

mkdir -p "$KEY_DIR"
chmod 700 "$KEY_DIR"

# 32 bytes = 256-bit key, matching LocalFileKeyProvider's ExpectedKeySizeBytes.
openssl rand 32 > "$KEY_PATH"
chmod 600 "$KEY_PATH"
chown "$(id -u):$(id -g)" "$KEY_PATH"

echo "Master key generated at $KEY_PATH (permissions 600)."

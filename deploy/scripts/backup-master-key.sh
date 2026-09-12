#!/usr/bin/env bash
# Backs up the Master Key file — deliberately separate from backup.sh (the database
# backup). docs/ForgeVault.md §56 ("regra de ouro"): never store the database backup and
# the Master Key backup in the same place. FORGEVAULT_MASTER_KEY_BACKUP_DIR has no default
# for exactly that reason — an operator must consciously point it somewhere else.
set -euo pipefail

KEY_PATH="${FORGEVAULT_MASTER_KEY_PATH:-/root/.forgevault/master.key}"
BACKUP_DIR="${FORGEVAULT_MASTER_KEY_BACKUP_DIR:?Set FORGEVAULT_MASTER_KEY_BACKUP_DIR to a location physically separate from the database backup directory (docs/ForgeVault.md §56).}"

if [ ! -f "$KEY_PATH" ]; then
  echo "No master key found at $KEY_PATH — nothing to back up." >&2
  exit 1
fi

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
DEST="$BACKUP_DIR/master-$TIMESTAMP.key"
cp "$KEY_PATH" "$DEST"
chmod 600 "$DEST"

echo "Master key backed up to $DEST"

#!/usr/bin/env bash
# Restores a ForgeVault database backup, and optionally a Master Key backup for full
# disaster recovery. Run against the Postgres container the same way backup.sh writes to
# it, to keep client/server versions matched (docs/ForgeVault.md §144-145).
#
# A restore drill (docs/modules/10_RESILIENCE_HA_OPERATIONS.md §3 UC-02) is not complete
# until you confirm a known secret still decrypts correctly after this script finishes —
# this script restores bytes, it does not itself prove they're still meaningful.
set -euo pipefail

DB_BACKUP_PATH="${1:?Usage: restore.sh <db-backup-file> [master-key-backup-file]}"
MASTER_KEY_BACKUP_PATH="${2:-}"
CONTAINER="${FORGEVAULT_POSTGRES_CONTAINER:-forgevault-postgres-1}"
POSTGRES_USER="${POSTGRES_USER:-forgevault}"
POSTGRES_DB="${POSTGRES_DB:-forgevault}"

echo "Restoring database from $DB_BACKUP_PATH into '$POSTGRES_DB' (container: $CONTAINER) ..."
docker exec -i "$CONTAINER" pg_restore --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --clean --if-exists < "$DB_BACKUP_PATH"
echo "Database restored."

if [ -n "$MASTER_KEY_BACKUP_PATH" ]; then
  KEY_PATH="${FORGEVAULT_MASTER_KEY_PATH:-/root/.forgevault/master.key}"
  if [ -e "$KEY_PATH" ]; then
    echo "Master key already exists at $KEY_PATH — refusing to overwrite it during restore." >&2
    exit 1
  fi
  mkdir -p "$(dirname "$KEY_PATH")"
  cp "$MASTER_KEY_BACKUP_PATH" "$KEY_PATH"
  chmod 600 "$KEY_PATH"
  echo "Master key restored to $KEY_PATH."
else
  echo "No master key backup provided. If the Master Key currently at \$FORGEVAULT_MASTER_KEY_PATH is not the SAME one that encrypted this database's secrets, every secret value in the restored database is permanently unreadable — this is only safe when the key was never lost. Pass the master key backup as the second argument for a genuine disaster-recovery restore."
fi

echo "Restore complete. Now run a decrypt drill: fetch a known secret's value through the API and confirm it matches."

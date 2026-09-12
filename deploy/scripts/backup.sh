#!/usr/bin/env bash
# Backs up the ForgeVault database (encrypted secrets, audit logs, everything else).
#
# Runs pg_dump *inside* the Postgres container (docker-compose.yml) rather than assuming a
# host-installed client — this guarantees a client/server version match without requiring
# every operator to install a matching postgresql-client package (docs/ForgeVault.md §144).
#
# docs/ForgeVault.md §56 ("regra de ouro"): this script never touches the Master Key. Back
# it up separately with deploy/scripts/backup-master-key.sh, to a genuinely different
# location — a backup that pairs the DB dump with the Master Key defeats the point of
# separating them.
set -euo pipefail

BACKUP_DIR="${FORGEVAULT_BACKUP_DIR:-./backups}"
CONTAINER="${FORGEVAULT_POSTGRES_CONTAINER:-forgevault-postgres-1}"
POSTGRES_USER="${POSTGRES_USER:-forgevault}"
POSTGRES_DB="${POSTGRES_DB:-forgevault}"
TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
DB_BACKUP_PATH="$BACKUP_DIR/forgevault-db-$TIMESTAMP.dump"

mkdir -p "$BACKUP_DIR"

docker exec "$CONTAINER" pg_dump --username="$POSTGRES_USER" --dbname="$POSTGRES_DB" --format=custom > "$DB_BACKUP_PATH"

echo "Database backup written to $DB_BACKUP_PATH"
echo "Remember: back up the Master Key separately (deploy/scripts/backup-master-key.sh) to a different location — never alongside this file."

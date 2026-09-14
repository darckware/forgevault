#!/usr/bin/env bash
# Builds api/web with real build identity (GET /api/v1/system/version) instead of the
# "unknown" fallback a bare `docker compose build` produces — same pattern as ForgeHub's own
# scripts/build.sh.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/../.."

export FORGEVAULT_VERSION="$(cat VERSION)"
export FORGEVAULT_GIT_SHA="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
export FORGEVAULT_BUILD_DATE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

docker compose build

docker tag "forgevault-api:latest" "forgevault-api:${FORGEVAULT_VERSION}"
docker tag "forgevault-web:latest" "forgevault-web:${FORGEVAULT_VERSION}"

echo "Built ForgeVault ${FORGEVAULT_VERSION} @ ${FORGEVAULT_GIT_SHA}"

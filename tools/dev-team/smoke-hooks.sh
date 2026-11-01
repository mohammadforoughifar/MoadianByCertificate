#!/usr/bin/env bash
# تست سریع health + CI failure
# Usage:
#   export DEVTEAM_API_BASE=https://erp.example.com
#   export DEVTEAM_GIT_WEBHOOK_SECRET=...
#   ./smoke-hooks.sh

set -euo pipefail
BASE="${DEVTEAM_API_BASE:?set DEVTEAM_API_BASE}"
BASE="${BASE%/}"
TOKEN="${DEVTEAM_GIT_WEBHOOK_SECRET:-}"

echo "== health =="
curl -sS "$BASE/api/dev-team/hooks/health" | head -c 2000
echo
echo

if [[ -z "$TOKEN" ]]; then
  echo "DEVTEAM_GIT_WEBHOOK_SECRET خالی است — فقط health چک شد."
  exit 0
fi

echo "== CI failure sample =="
curl -sS -X POST "$BASE/api/dev-team/hooks/ci" \
  -H "Content-Type: application/json" \
  -H "X-DevTeam-Token: $TOKEN" \
  -d '{
    "provider":"smoke",
    "status":"failure",
    "jobName":"smoke-hooks",
    "repo":"local/smoke",
    "branch":"main",
    "commitMessage":"smoke CI failure DT-0000-0000",
    "url":"https://example.local/smoke"
  }'
echo
echo "OK — در تب مشکلات DevTeam یک Error جدید ببینید (dedupe با title)."

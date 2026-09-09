#!/usr/bin/env bash
# Refresh Original Path-B snapshot (if Editor is up) and print a cheap summary.
set -euo pipefail
PROJECT="${WRLDZ_PROJECT:-/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON}"
cd "$PROJECT"

refresh=1
if [[ "${1:-}" == "--cluster-only" ]]; then
  refresh=0
fi

if [[ "$refresh" == 1 ]]; then
  st="$(unity status --format json --no-banner --non-interactive 2>/dev/null || true)"
  ready="$(python3 -c '
import json,sys
try:
    d=json.loads(sys.argv[1])
    inst=(d.get("data") or {}).get("instances") or []
    print("yes" if inst and inst[0].get("state")=="ready" else "no")
except Exception:
    print("no")
' "$st")"
  if [[ "$ready" == "yes" ]]; then
    echo "== wrldz_original_status =="
    unity run "$PROJECT" --command wrldz_original_status --timeout 180 \
      --no-banner --non-interactive --format json >/tmp/wrldz_original_status.json || true
  else
    echo "== Editor not ready; clustering last snapshot =="
  fi
fi

echo "== cluster =="
python3 "$PROJECT/Tools/original_pool_cluster.py"
echo "== markdown =="
sed -n '1,40p' "$PROJECT/Assets/StreamingAssets/WRLDZ/eras/original_status.md" 2>/dev/null || true

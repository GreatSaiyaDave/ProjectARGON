#!/usr/bin/env bash
# WRLDZ verify pipeline — run TCG regressions even when Unity Editor is already open.
# Prefers a live Pipeline server (~200ms). Falls back to headless `unity run --command`.
set -euo pipefail

PROJECT="${WRLDZ_PROJECT:-/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON}"
EXTRACT_LUA=0
TIMEOUT="${WRLDZ_VERIFY_TIMEOUT:-180}"

usage() {
  cat <<EOF
Usage: $(basename "$0") [--extract-lua] [--timeout SECONDS]

  --extract-lua   Rebuild Lua fact seeds (continuous, triggers, ST facts, puzzles)
                  from vendored OcgCore official scripts / Ignis puzzles.
  --timeout N     Seconds to wait for Unity (default ${TIMEOUT}).
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --extract-lua) EXTRACT_LUA=1; shift ;;
    --timeout) TIMEOUT="${2:?}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

cd "$PROJECT"

if [[ "$EXTRACT_LUA" == 1 ]]; then
  echo "== extract YGOPro catalogs (official scripts, facts only) =="
  python3 "$PROJECT/Tools/extract_ygopro_continuous.py" \
    "$PROJECT/Assets/StreamingAssets/WRLDZ/ygopro_continuous_seed_v1.json"
  python3 "$PROJECT/Tools/extract_ygopro_triggers.py" \
    "$PROJECT/Assets/StreamingAssets/WRLDZ/ygopro_trigger_seed_v1.json"
  python3 "$PROJECT/Tools/extract_ygopro_st_facts.py" \
    "$PROJECT/Assets/StreamingAssets/WRLDZ/ygopro_st_facts_v1.json"
  python3 "$PROJECT/Tools/extract_puzzle_facts.py" \
    "$PROJECT/Assets/StreamingAssets/WRLDZ/puzzle_facts_v1.json"
fi

echo "== engine gap scan (cards_db vs compiler regex vs Lua seed) =="
python3 "$PROJECT/Tools/scan_engine_gaps.py"

echo "== Equip / unplayable stress (template vs leftover) =="
python3 "$PROJECT/Tools/stress_unplayable_cards.py"

echo "== Unity Pipeline =="
PIPE_JSON="$(unity pipeline list --format json --no-banner --non-interactive 2>/dev/null || true)"
python3 -c '
import json,sys
raw=sys.argv[1]
try:
    d=json.loads(raw)
except Exception:
    print("pipeline list: unreadable")
    raise SystemExit(0)
data=d.get("data") or {}
summary=data.get("summary") or {}
print("running Editors:", summary.get("runningInstances", 0),
      "with Pipeline:", summary.get("instancesWithPipeline", 0),
      "reachable:", summary.get("reachableServers", 0),
      "Safe Mode:", summary.get("instancesInSafeMode", 0))
for inst in data.get("instances") or []:
    print(" ", inst.get("projectName"), "pid", inst.get("pid"),
          "pipeline", inst.get("hasPipelinePackage"),
          "reachable", (inst.get("pipelineServer") or {}).get("isReachable"),
          "safeMode", inst.get("safeMode"))
' "$PIPE_JSON" || true

LOG="$PROJECT/Logs/wrldz-verify.log"
mkdir -p "$PROJECT/Logs"

# unity run --command reuses an already-open Editor when Pipeline is up.
echo "== wrldz_tcg_tests =="
CODE=1
ATTEMPT=1
MAX_ATTEMPTS=4
while [[ "$ATTEMPT" -le "$MAX_ATTEMPTS" ]]; do
  echo "attempt $ATTEMPT/$MAX_ATTEMPTS: unity run --command wrldz_tcg_tests"
  set +e
  unity run "$PROJECT" --command wrldz_tcg_tests --format json --timeout "$TIMEOUT" \
    --no-banner --non-interactive >"$LOG" 2>&1
  CODE=$?
  set -e
  if [[ "$CODE" -eq 0 ]]; then
    break
  fi
  if grep -q '503 Service Unavailable\|Server Busy\|still settling' "$LOG"; then
    echo "Pipeline busy (compile/import). waiting 20s..."
    sleep 20
    ATTEMPT=$((ATTEMPT + 1))
    continue
  fi
  break
done
echo "unity --command exit: $CODE"
tail -n 40 "$LOG" || true

if [[ "$CODE" -ne 0 ]]; then
  echo "== fallback: -executeMethod TcgEngineTestsMenu.RunTestsBatch =="
  set +e
  unity run "$PROJECT" --timeout "$TIMEOUT" --no-banner --non-interactive -- \
    -nographics -logFile "$PROJECT/Logs/wrldz-verify-batch.log" \
    -executeMethod WRLDZ.EditorTools.TcgEngineTestsMenu.RunTestsBatch
  CODE=$?
  set -e
  echo "unity executeMethod exit: $CODE"
  grep -E 'WRLDZ TCG Tests|PASS  |FAIL  |error CS' "$PROJECT/Logs/wrldz-verify-batch.log" \
    | tail -n 80 || true
  exit "$CODE"
fi

python3 -c '
import json,sys,re
raw=open(sys.argv[1], encoding="utf-8", errors="replace").read()
objs=[]
dec=json.JSONDecoder()
i=0
s=raw
while i < len(s):
    m=re.search(r"\{", s[i:])
    if not m:
        break
    start=i+m.start()
    try:
        obj, end = dec.raw_decode(s, start)
        objs.append(obj)
        i=end
    except json.JSONDecodeError:
        i=start+1
if not objs:
    print("NO_JSON_RESULT")
    raise SystemExit(1)
env=objs[-1]
ok=bool(env.get("success"))
data=env.get("data") or {}
result=data.get("result")
print("envelope success:", ok, "reusedRunningEditor:", data.get("reusedRunningEditor"))
if isinstance(result, str):
    lines=result.strip().splitlines()
    print("--- report tail ---")
    print("\n".join(lines[-40:]))
    if any(l.startswith("FAIL  ") for l in lines):
        raise SystemExit(1)
if not ok:
    print("errors:", env.get("errors") or [])
    raise SystemExit(1)
print("WRLDZ_VERIFY_OK")
' "$LOG"
exit $?

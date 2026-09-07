#!/usr/bin/env bash
# Idempotent Cloud Agent bootstrap for Project ARGON (WRLDZ).
#
# The offline TCG verification pipeline (engine gap scan + unplayable-card
# stress) is pure Python stdlib, so there is nothing to `pip install`. This
# script only verifies that the toolchain and committed data the pipeline
# depends on are present, and byte-compiles the tools to catch syntax errors
# early. It is safe to run repeatedly and always terminates.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

echo "== toolchain =="
python3 --version
cmake --version | head -1
g++ --version | head -1
git --version

echo "== required data =="
required=(
  "Assets/StreamingAssets/Cards/cards_db.json"
  "Assets/StreamingAssets/WRLDZ/ygopro_continuous_seed_v1.json"
  "Assets/StreamingAssets/WRLDZ/ygopro_trigger_seed_v1.json"
  "Assets/StreamingAssets/WRLDZ/ygopro_st_facts_v1.json"
  "Assets/StreamingAssets/WRLDZ/puzzle_facts_v1.json"
  "Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json"
)
missing=0
for f in "${required[@]}"; do
  if [[ -f "$f" ]]; then
    echo "  ok   $f"
  else
    echo "  MISS $f"
    missing=1
  fi
done
if [[ "$missing" -ne 0 ]]; then
  echo "error: required data files are missing" >&2
  exit 1
fi

echo "== byte-compile Python tools =="
python3 -m py_compile \
  Tools/scan_engine_gaps.py \
  Tools/stress_unplayable_cards.py
echo "  ok"

echo "install complete"

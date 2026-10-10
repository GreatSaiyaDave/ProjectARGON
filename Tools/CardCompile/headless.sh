#!/usr/bin/env bash
# headless.sh <tree> [HeadlessEngine args…]
#
# Build and run the headless engine inside <tree> (a scratch_tree.sh copy, or the real checkout: `.`)
# with a private, freshly wiped compiled-program disk cache, so no run ever sees another tree's programs.
#
# The persist dir is unique per run (mktemp + trap EXIT), so concurrent runs never wipe each other's
# live compiled-program cache; the server serializes them further with a flock (headless.lock).
#
#   Tools/CardCompile/headless.sh "$T" --quiet                 # full suite (exit 1 on any FAIL)
#   Tools/CardCompile/headless.sh "$T" --card "Brain Control"  # how one card compiles
#   Tools/CardCompile/headless.sh "$T" --status-dump > s.tsv   # every card's status (worklist / diffs)
#   Tools/CardCompile/headless.sh "$T" --export-seed           # rewrite <tree>'s compiled-effects seed
#   Tools/CardCompile/headless.sh . --prelink                  # compile eras/prelink_monsters.json
#   Tools/CardCompile/headless.sh . --prelink-card "Dark Magician"  # one monster from that catalog
set -euo pipefail

tree="$(cd "${1:?usage: headless.sh <tree> [args…]}" && pwd)"
shift
persist="$(mktemp -d "${TMPDIR:-/tmp}/wrldz-persist-XXXXXX")"
cleanup() {
    # /tmp is a quota'd tmpfs, and 180+ leftover caches (1.6 GB) once filled it (2026-09-30).
    rm -rf "$persist"
}
trap cleanup EXIT
export WRLDZ_PERSIST="$persist" WRLDZ_REPO_ROOT="$tree"
# Run (not exec) so the private cache is removed afterwards. The exit code is passed through.
rc=0
bash "$tree/Tools/HeadlessEngine/run.sh" "$@" || rc=$?
exit "$rc"

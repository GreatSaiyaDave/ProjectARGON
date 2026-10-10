#!/usr/bin/env bash
# scratch_tree.sh <dest> [<git-ref>]
#
# An isolated copy of just what the headless engine needs (all of Assets/Scripts/WRLDZ, Tools/HeadlessEngine,
# Tools/CardCompile, cards_db, decks, eras, the top-level WRLDZ + OcgCore data files) so one card batch can be
# implemented and tested without touching the real checkout or another batch.
#
#   - Source: the real checkout's working tree (default: uncommitted work included), or a git ref.
#   - The compiled-effects seed is left out, so every card compiles fresh from the batch's templates
#     (a seed at the same compiler Version would otherwise serve the old programs).
#   - The copy is its own git repo with one "baseline" commit: `git -C <dest> diff` IS the batch's patch.
#
# Prints <dest>. Reference-only data stays in the real checkout (read it there, never copy it):
# the official ygopro Lua scripts at Assets/StreamingAssets/OcgCore/scripts/official~/c<id>.lua.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dest="${1:?usage: scratch_tree.sh <dest> [<git-ref>]}"
ref="${2:-WORKTREE}"

rm -rf "$dest"
mkdir -p "$dest"
dest="$(cd "$dest" && pwd)"
cd "$REPO"

DIRS=(Assets/Scripts/WRLDZ Tools/HeadlessEngine Tools/CardCompile Assets/StreamingAssets/Cards
      Assets/StreamingAssets/Decks Assets/StreamingAssets/WRLDZ/eras)
TOPS=(Assets/StreamingAssets/WRLDZ Assets/StreamingAssets/OcgCore)

if [[ "$ref" == WORKTREE ]]; then
  {
    for d in "${DIRS[@]}"; do
      [[ -d "$d" ]] && find "$d" -type f -not -path "*/bin/*" -not -path "*/obj/*"
    done
    for t in "${TOPS[@]}"; do
      [[ -d "$t" ]] && find "$t" -maxdepth 1 -type f
    done
  } | tar -cf - -T - | tar -xf - -C "$dest"
else
  paths=()
  for d in "${DIRS[@]}"; do git cat-file -e "$ref:$d" 2>/dev/null && paths+=("$d"); done
  for t in "${TOPS[@]}"; do
    while IFS= read -r f; do
      [[ "$(git cat-file -t "$ref:$f" 2>/dev/null)" == blob ]] && paths+=("$f")
    done < <(git ls-tree --name-only "$ref" "$t/")
  done
  git archive --format=tar "$ref" -- "${paths[@]}" | tar -xf - -C "$dest"
fi

rm -f "$dest/Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json"

cat > "$dest/.gitignore" <<'EOF'
bin/
obj/
.persist/
EOF
git -C "$dest" init -q
git -C "$dest" add -A
git -C "$dest" -c user.name=card-batch -c user.email=card-batch@localhost -c commit.gpgsign=false \
  commit -q -m "baseline ($ref)"
echo "$dest"

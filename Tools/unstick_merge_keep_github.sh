#!/usr/bin/env bash
# Make THIS folder match GitHub main. This is NOT an upload.
# If the owner wanted the PC copy kept / sent to GitHub, use
# Tools/restore_pc_folder_from_before_unstick.sh instead.
# Copies a few unique local files back if they are still on disk.
# Does not force-push. Does not delete the Unity folder.
set -euo pipefail

say() { printf '%s\n' "$*"; }
die() { say ""; say "Stopped. $*"; say "Nothing was force-pushed. The Unity folder is still here."; exit 1; }

if [[ ! -d Assets || ! -d ProjectSettings ]]; then
  die "Open Terminal in the Unity project folder (the one that has Assets and ProjectSettings)."
fi
say "Copying GitHub onto THIS folder. This is not an upload."
say ""
if [[ -f Temp/UnityLockfile ]]; then
  die "Close Unity Hub / Unity Editor first. Then run this again in the same folder."
fi
if ! command -v git >/dev/null 2>&1; then
  die "Git is missing. In Terminal:  sudo apt update && sudo apt install git"
fi
if [[ ! -d .git ]]; then
  die "This folder is not a git project."
fi

remote="$(git remote get-url origin 2>/dev/null || true)"
if [[ -z "$remote" ]]; then
  die "This folder has no GitHub remote named origin."
fi
if [[ "$remote" != *ProjectARGON* ]]; then
  die "origin does not look like ProjectARGON ($remote)."
fi

if [[ -f .git/MERGE_HEAD ]]; then
  say "Leaving the unfinished merge so your files stay on disk."
  git merge --abort
  say "Merge unstuck. Files were not deleted."
fi

KEEP_ROOT="$(mktemp -d /tmp/wrldz-keep-XXXXXX)"
cleanup_keep() { rm -rf "$KEEP_ROOT"; }
trap cleanup_keep EXIT

copy_keep() {
  local rel="$1"
  if [[ -e "$rel" ]]; then
    mkdir -p "$KEEP_ROOT/$(dirname "$rel")"
    cp -a "$rel" "$KEEP_ROOT/$rel"
    say "Saved a copy of $rel"
  fi
}

# Unique local work that GitHub does not already have.
# Do not keep official~ (Unity backup of lab Lua). Do not keep _argon scratch scripts.
copy_keep "Assets/StreamingAssets/WRLDZ/Imagine/fx/warning_respond.png"
copy_keep "Assets/StreamingAssets/WRLDZ/Imagine/fx/warning_respond.png.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/Story.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/Story/season1_dk.json"
copy_keep "Assets/StreamingAssets/WRLDZ/Story/season1_dk.json.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_capability_backlog.md"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_capability_backlog.md.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_status.json"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_status.json.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_status.md"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/original_status.md.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/supported_original.json"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/supported_original.json.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/text.meta"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/text/original.json"
copy_keep "Assets/StreamingAssets/WRLDZ/eras/text/original.json.meta"
copy_keep "Docs/HELPER_BOT_HANDOFF_2026-09-07.md"
copy_keep "Docs/OCG_LAB_VERIFY_2026-09-07.md"
copy_keep "Docs/RELINQUISHED_STRESS_2026-09-07.md"
copy_keep "Docs/STRESS_Relinquished_20260907_182620.md"
copy_keep "Docs/STRESS_Relinquished_20260907_184618.md"
copy_keep "Tools/original-pool-report.sh"
copy_keep "Tools/original_pool_cluster.py"
copy_keep "Tools/ui_preview_filament_kit.png"
copy_keep "Tools/wrldz_deckgen/lflist_april2005.json"

say "Matching this folder to GitHub main (shared files take GitHub's version)."
git fetch origin main
git reset --hard origin/main

official_bak="Assets/StreamingAssets/OcgCore/scripts/official~"
if [[ -e "$official_bak" ]]; then
  rm -rf "$official_bak"
  say "Removed leftover Unity backup folder official~ (lab Lua stays in official/)."
fi

restored=0
if [[ -d "$KEEP_ROOT" ]]; then
  while IFS= read -r -d '' saved; do
    rel="${saved#"$KEEP_ROOT"/}"
    mkdir -p "$(dirname "$rel")"
    cp -a "$saved" "$rel"
    say "Put back $rel"
    restored=1
  done < <(find "$KEEP_ROOT" -type f -print0 2>/dev/null || true)
fi

if [[ "$restored" -eq 1 ]]; then
  git add -- \
    Assets/StreamingAssets/WRLDZ/Imagine/fx/warning_respond.png \
    Assets/StreamingAssets/WRLDZ/Imagine/fx/warning_respond.png.meta \
    Assets/StreamingAssets/WRLDZ/Story.meta \
    Assets/StreamingAssets/WRLDZ/Story/season1_dk.json \
    Assets/StreamingAssets/WRLDZ/Story/season1_dk.json.meta \
    Assets/StreamingAssets/WRLDZ/eras \
    Docs/HELPER_BOT_HANDOFF_2026-09-07.md \
    Docs/OCG_LAB_VERIFY_2026-09-07.md \
    Docs/RELINQUISHED_STRESS_2026-09-07.md \
    Docs/STRESS_Relinquished_20260907_182620.md \
    Docs/STRESS_Relinquished_20260907_184618.md \
    Tools/original-pool-report.sh \
    Tools/original_pool_cluster.py \
    Tools/ui_preview_filament_kit.png \
    Tools/wrldz_deckgen/lflist_april2005.json \
    2>/dev/null || true
  if ! git diff --cached --quiet; then
    git commit -m "Keep unique local files after send merge recovery $(date +%Y-%m-%d)"
    if ! git push origin main; then
      say ""
      say "Folder matches GitHub. Unique files were put back, but push needs GitHub login."
      say "Install once:  sudo apt install gh"
      say "Then:  gh auth login"
      say "Pick GitHub.com, HTTPS, Login with a web browser. Then run this recovery again."
      exit 1
    fi
    say "Unique local files are on GitHub too."
  fi
fi

say ""
say "Done. This folder matches GitHub again."
say "Open Unity Hub. Same project. Click Play."
say "If Hub still looks old: WRLDZ menu → Pull latest from GitHub (origin/main)."

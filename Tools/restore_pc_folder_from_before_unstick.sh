#!/usr/bin/env bash
# Put THIS Unity folder back to the snapshot from just before unstick
# (the send-to-GitHub save), then remove leftover GitHub files and the
# Unity Library so Hub is not a mix. Does not upload. Does not force-push.
# Does not delete the Unity folder.
set -euo pipefail

say() { printf '%s\n' "$*"; }
die() { say ""; say "Stopped. $*"; say "Nothing was uploaded. The Unity folder is still here."; exit 1; }

trap 'say ""; say "Stopped unexpectedly at line $LINENO. Paste this whole Terminal output."; exit 1' ERR

say "Restore starting in $(pwd)"

if [[ ! -d Assets || ! -d ProjectSettings ]]; then
  die "Open Terminal in the Unity project folder (the one that has Assets and ProjectSettings)."
fi
if [[ -f Temp/UnityLockfile ]]; then
  die "Close Unity Hub / Unity Editor first. Then run this again in the same folder."
fi
if ! command -v git >/dev/null 2>&1; then
  die "Git is missing. In Terminal:  sudo apt update && sudo apt install git"
fi
if [[ ! -d .git ]]; then
  die "This folder is not a git project."
fi

remote="$(git remote get-url origin </dev/null 2>/dev/null || true)"
if [[ -z "$remote" ]]; then
  die "This folder has no GitHub remote named origin."
fi
if [[ "$remote" != *ProjectARGON* ]]; then
  die "origin does not look like ProjectARGON ($remote)."
fi

current="$(git rev-parse HEAD </dev/null)" || die "Git cannot read HEAD in this folder."
say "Now at: $(git log -1 --format='%h %s' HEAD </dev/null)"

if [[ -f .git/MERGE_HEAD ]]; then
  say "Leaving the unfinished merge. Your saved folder comes back."
  git merge --abort </dev/null
  current="$(git rev-parse HEAD </dev/null)"
  say "After abort: $(git log -1 --format='%h %s' HEAD </dev/null)"
fi

is_send_commit() {
  local sha="${1:-}"
  local subj
  [[ -n "$sha" ]] || return 1
  git cat-file -t "$sha" </dev/null >/dev/null 2>&1 || return 1
  [[ "$(git cat-file -t "$sha" </dev/null 2>/dev/null || true)" == "commit" ]] || return 1
  git rev-parse --verify "$sha:Assets" </dev/null >/dev/null 2>&1 || return 1
  git rev-parse --verify "$sha:ProjectSettings" </dev/null >/dev/null 2>&1 || return 1
  subj="$(git log -1 --format='%s' "$sha" </dev/null 2>/dev/null || true)"
  [[ "$subj" == "Local Unity Hub folder"* ]] || return 1
  return 0
}

pick_reflog() {
  set +o pipefail
  git reflog --format='%H %gs' </dev/null | awk "$1"
  set -o pipefail
  return 0
}

snapshot=""
send_sha="$(pick_reflog '/commit: Local Unity Hub folder/ { print $1; exit }')"
say "Saved local copy in history: ${send_sha:-not found}"
if is_send_commit "$send_sha"; then
  snapshot="$send_sha"
fi

if [[ -z "$snapshot" && -f .git/ORIG_HEAD ]]; then
  orig="$(git rev-parse ORIG_HEAD </dev/null 2>/dev/null || true)"
  say "ORIG_HEAD: ${orig:-none}"
  if is_send_commit "$orig"; then
    snapshot="$orig"
  fi
fi

if [[ -z "$snapshot" ]]; then
  after_reset="$(pick_reflog 'found { print $1; exit } /reset: moving to origin\/main/ { found=1 }')"
  say "Folder from before unstick reset: ${after_reset:-not found}"
  if is_send_commit "$after_reset"; then
    snapshot="$after_reset"
  fi
fi

if [[ -z "$snapshot" ]] && is_send_commit "$current"; then
  snapshot="$current"
fi

if [[ -z "$snapshot" ]]; then
  say "Recent history:"
  git reflog </dev/null | head -n 20 || true
  die "Could not find the saved local folder from before unstick. Paste this Terminal output."
fi

say "Putting this folder back to your saved local copy:"
git log -1 --format='%h  %ci  %s' "$snapshot" </dev/null
say "Matching files only. GitHub is left alone."
git reset --hard "$snapshot" </dev/null

say "Removing leftover GitHub files that are not in your saved copy."
removed=0
if git rev-parse --verify origin/main </dev/null >/dev/null 2>&1; then
  while IFS= read -r path; do
    [[ -n "$path" ]] || continue
    if git cat-file -e "$snapshot:$path" </dev/null 2>/dev/null; then
      continue
    fi
    if [[ -e "$path" || -L "$path" ]]; then
      say "Removing GitHub leftover: $path"
      rm -rf -- "$path"
      removed=$((removed + 1))
    fi
  done < <(git ls-tree -r --name-only origin/main </dev/null)
fi
say "GitHub leftover files removed: $removed"

official="Assets/StreamingAssets/OcgCore/scripts/official"
official_bak="Assets/StreamingAssets/OcgCore/scripts/official~"
if [[ -e "$official_bak" && -e "$official" ]]; then
  say "Removing leftover GitHub lab folder official/ (keeping your official~ save)."
  rm -rf -- "$official"
fi

stamp="$(date +%Y%m%d-%H%M%S)"
if [[ -d Library ]]; then
  mv Library "Library_mix_${stamp}"
  say "Moved Unity Library aside (Library_mix_${stamp}) so Hub does not keep GitHub leftovers."
fi
if [[ -d Temp ]]; then
  rm -rf -- Temp
  say "Cleared Temp."
fi

trap - ERR
say ""
say "Done. This folder is your local copy again."
say "GitHub leftover files were removed. Unity will rebuild on next Play (slow once)."
say "GitHub was not changed."
say "Open Unity Hub. Same project. Click Play."
say "Do not Add a second project. Do not unzip a new copy."

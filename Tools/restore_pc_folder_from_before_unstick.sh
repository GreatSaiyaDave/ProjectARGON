#!/usr/bin/env bash
# Put THIS Unity folder back to the snapshot from just before unstick
# (the send-to-GitHub save). Does not upload. Does not force-push.
# Does not delete the Unity folder.
set -euo pipefail

say() { printf '%s\n' "$*"; }
die() { say ""; say "Stopped. $*"; say "Nothing was uploaded. The Unity folder is still here."; exit 1; }

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

remote="$(git remote get-url origin 2>/dev/null || true)"
if [[ -z "$remote" ]]; then
  die "This folder has no GitHub remote named origin."
fi
if [[ "$remote" != *ProjectARGON* ]]; then
  die "origin does not look like ProjectARGON ($remote)."
fi

current="$(git rev-parse HEAD)"
current_subj="$(git log -1 --format='%s' HEAD)"

# Still in the unfinished send merge: aborting it returns this folder
# to the local save. That is the copy you wanted to keep.
if [[ -f .git/MERGE_HEAD ]]; then
  say "Leaving the unfinished merge. Your saved folder comes back."
  git merge --abort
  say ""
  say "Done. This folder is your local copy again."
  say "GitHub was not changed."
  say "Open Unity Hub. Same project. Click Play."
  exit 0
fi

if [[ "$current_subj" == "Local Unity Hub folder"* ]]; then
  say "This folder already looks like your saved local copy."
  say "GitHub was not changed."
  say "Open Unity Hub. Same project. Click Play."
  exit 0
fi

is_send_snapshot() {
  local sha="$1"
  local subj
  [[ -n "$sha" ]] || return 1
  git cat-file -t "$sha" >/dev/null 2>&1 || return 1
  [[ "$(git cat-file -t "$sha" 2>/dev/null)" == "commit" ]] || return 1
  git rev-parse --verify "$sha:Assets" >/dev/null 2>&1 || return 1
  git rev-parse --verify "$sha:ProjectSettings" >/dev/null 2>&1 || return 1
  subj="$(git log -1 --format='%s' "$sha" 2>/dev/null || true)"
  [[ "$subj" == "Local Unity Hub folder"* ]] || return 1
  [[ "$(git rev-parse "$sha")" != "$current" ]] || return 1
  return 0
}

snapshot=""

# Newest send save still in this folder's history (not uploaded).
send_sha="$(
  git reflog --format='%H %gs' | awk '
    /commit: Local Unity Hub folder/ { print $1; exit }
  '
)"
if is_send_snapshot "$send_sha"; then
  snapshot="$send_sha"
fi

# After unstick's reset, ORIG_HEAD is the local save — only if it is that save.
if [[ -z "$snapshot" && -f .git/ORIG_HEAD ]]; then
  orig="$(git rev-parse ORIG_HEAD 2>/dev/null || true)"
  if is_send_snapshot "$orig"; then
    snapshot="$orig"
  fi
fi

# Next reflog line after "reset: moving to origin/main" is the pre-unstick folder.
if [[ -z "$snapshot" ]]; then
  after_reset="$(
    git reflog --format='%H %gs' | awk '
      found { print $1; exit }
      /reset: moving to origin\/main/ { found=1 }
    '
  )"
  if is_send_snapshot "$after_reset"; then
    snapshot="$after_reset"
  fi
fi

if [[ -z "$snapshot" ]]; then
  die "Could not find the saved local folder from before unstick. Paste this Terminal output for the agent."
fi

say "Putting this folder back to your saved local copy:"
git log -1 --format='%h  %ci  %s' "$snapshot"
say "Matching files only. GitHub is left alone."
git reset --hard "$snapshot"

say ""
say "Done. This folder is your local copy again."
say "GitHub was not changed."
say "Open Unity Hub. Same project. Click Play."
say "Do not Add a second project. Do not unzip a new copy."

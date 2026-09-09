#!/usr/bin/env bash
# Put THIS Unity folder back to the snapshot from just before unstick
# (the send-to-GitHub save), then remove leftover GitHub files and the
# Unity Library so Hub is not a mix. Does not fetch, pull, upload, or
# force-push. Does not delete the Unity folder.
set -euo pipefail

REPORT=/tmp/wrldz-restore-report.txt
: > "$REPORT"
say() { printf '%s\n' "$*" | tee -a "$REPORT"; }
die() { say ""; say "Stopped. $*"; say "Nothing was uploaded. The Unity folder is still here."; say "Paste $REPORT"; exit 1; }

trap 'say ""; say "Stopped unexpectedly at line $LINENO. Paste '"$REPORT"'."; exit 1' ERR

say "Restore starting in $(pwd)"
say "Report: $REPORT"

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

github="$(git rev-parse origin/main </dev/null 2>/dev/null || true)"
current="$(git rev-parse HEAD </dev/null)" || die "Git cannot read HEAD in this folder."
say "Now at: $(git log -1 --format='%h %s' HEAD </dev/null)"
if [[ -n "$github" ]]; then
  say "GitHub main: $(git log -1 --format='%h %s' origin/main </dev/null)"
  if [[ "$current" == "$github" ]]; then
    say "This folder currently matches GitHub. That is the broken copy."
  fi
fi
say "Stashes: $(git stash list </dev/null | wc -l)"

if [[ -f .git/MERGE_HEAD ]]; then
  say "Leaving the unfinished merge. Your saved folder comes back."
  git merge --abort </dev/null
  current="$(git rev-parse HEAD </dev/null)"
  say "After abort: $(git log -1 --format='%h %s' HEAD </dev/null)"
fi

usable_tree() {
  local sha="${1:-}"
  [[ -n "$sha" ]] || return 1
  git cat-file -t "$sha" </dev/null >/dev/null 2>&1 || return 1
  [[ "$(git cat-file -t "$sha" </dev/null 2>/dev/null || true)" == "commit" ]] || return 1
  git rev-parse --verify "$sha:Assets" </dev/null >/dev/null 2>&1 || return 1
  git rev-parse --verify "$sha:ProjectSettings" </dev/null >/dev/null 2>&1 || return 1
  return 0
}

is_github_or_keep() {
  local sha="${1:-}"
  local subj
  [[ -n "$sha" ]] || return 0
  if [[ -n "$github" && "$sha" == "$github" ]]; then
    return 0
  fi
  subj="$(git log -1 --format='%s' "$sha" </dev/null 2>/dev/null || true)"
  [[ "$subj" == "Keep unique local files after send merge recovery"* ]] && return 0
  return 1
}

cands=()
add_cand() {
  local sha="${1:-}"
  local why="${2:-}"
  sha="$(git rev-parse "$sha" </dev/null 2>/dev/null || true)"
  usable_tree "$sha" || return 0
  if is_github_or_keep "$sha"; then
    return 0
  fi
  local x
  for x in "${cands[@]+"${cands[@]}"}"; do
    if [[ "$x" == "$sha" ]]; then
      return 0
    fi
  done
  cands+=("$sha")
  say "Candidate ($why): $(git log -1 --format='%h %s' "$sha" </dev/null)"
}

set +o pipefail
while IFS= read -r line; do
  sha="${line%% *}"
  gs="${line#* }"
  if [[ "$gs" == "commit: Local Unity Hub folder"* ]]; then
    add_cand "$sha" "send save"
  fi
done < <(git reflog --format='%H %gs' </dev/null | head -n 80)
set -o pipefail

if [[ -f .git/ORIG_HEAD ]]; then
  add_cand "$(git rev-parse ORIG_HEAD </dev/null 2>/dev/null || true)" "ORIG_HEAD"
fi

pre_unstick="$(
  set +o pipefail
  git reflog --format='%H %gs' </dev/null | awk '
    found { print $1; exit }
    /reset: moving to origin\/main/ { found=1 }
  '
  set -o pipefail
)"
add_cand "$pre_unstick" "before unstick reset"

snapshot=""
bestn=-1
if [[ ${#cands[@]} -eq 0 ]]; then
  say "Recent history:"
  git reflog </dev/null | head -n 25 | tee -a "$REPORT" || true
  die "Could not find the saved local folder from before unstick."
fi

if [[ -z "$github" ]]; then
  snapshot="${cands[0]}"
else
  for sha in "${cands[@]}"; do
    n="$(git diff --name-only "$github" "$sha" </dev/null | wc -l)"
    n="${n// /}"
    say "Files different from GitHub at $(git rev-parse --short "$sha" </dev/null): $n"
    if [[ "$n" -gt "$bestn" ]]; then
      bestn="$n"
      snapshot="$sha"
    fi
  done
  if [[ -z "$snapshot" || "$bestn" -lt 1 ]]; then
    die "Every saved copy looks like GitHub. Cannot restore local files from git."
  fi
fi

say "Putting this folder back to your saved local copy:"
git log -1 --format='%h  %ci  %s' "$snapshot" </dev/null | tee -a "$REPORT"
say "This is not GitHub. Do not click Get Latest from GitHub after this."
git reset --hard "$snapshot" </dev/null
current="$(git rev-parse HEAD </dev/null)"
if [[ -n "$github" && "$current" == "$github" ]]; then
  die "Reset still landed on GitHub. Paste $REPORT"
fi

say "Removing leftover GitHub files that are not in your saved copy."
removed=0
if [[ -n "$github" ]]; then
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

if [[ -n "$github" ]]; then
  say "Files still different from GitHub: $(git diff --name-only origin/main HEAD </dev/null | wc -l | tr -d ' ')"
  git diff --shortstat origin/main HEAD </dev/null | tee -a "$REPORT" || true
fi

trap - ERR
say ""
say "Done. This folder is the save from just before GitHub was copied onto it."
say "Fixes typed after that send, and not saved, cannot come back from git."
say "GitHub was not changed."
say "Open Unity Hub. Same project. Click Play. First open can be slow."
say "Do not use WRLDZ → Get Latest from GitHub. That copies GitHub back."
say "Do not Add a second project. Do not unzip a new copy."
say "Leave this Terminal text. Also saved at $REPORT"

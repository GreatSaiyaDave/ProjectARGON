#!/usr/bin/env bash
# Commit THIS Unity folder and push it to GitHub main.
# Merges GitHub first so cloud-agent work is not wiped.
# Does not replace the folder. Does not force-push.
set -euo pipefail

ROOT="$(pwd)"
if [[ ! -d "$ROOT/Assets" || ! -d "$ROOT/ProjectSettings" ]]; then
  echo "Run this inside your Unity project folder (the one that contains Assets)."
  echo "Now in: $ROOT"
  exit 1
fi

if [[ -f "$ROOT/Temp/UnityLockfile" ]]; then
  echo "Unity is still open. Close Unity (File → Exit), then run this again."
  echo "Or, if you already have WRLDZ → Get Latest from GitHub, you can use"
  echo "WRLDZ → Send this folder to GitHub from the Unity menu instead."
  exit 1
fi

if ! command -v git >/dev/null 2>&1; then
  echo "Git is not installed. In Terminal run: sudo apt install git"
  exit 1
fi

ORIGIN_URL="https://github.com/GreatSaiyaDave/ProjectARGON.git"

ensure_git_identity() {
  if ! git config user.email >/dev/null 2>&1; then
    git config user.email "owner@localhost"
    git config user.name "WRLDZ owner"
  fi
}

commit_local_if_needed() {
  git add -A
  if git diff --cached --quiet; then
    return 0
  fi
  git commit -m "Local Unity Hub folder $(date -u +%Y-%m-%d)"
}

merge_github() {
  git fetch origin
  extra=()
  if ! git merge-base HEAD origin/main >/dev/null 2>&1; then
    extra=(--allow-unrelated-histories)
  fi
  if git merge origin/main --no-edit "${extra[@]}"; then
    return 0
  fi
  echo
  echo "Stopped. GitHub and your folder both changed the same files."
  echo "Nothing was deleted. Tell the agent: merge conflict in ProjectARGON."
  echo "Paste this Terminal output."
  exit 1
}

push_main() {
  if git push origin main; then
    return 0
  fi
  echo
  echo "Your files are saved in this folder. GitHub did not accept the upload"
  echo "(sign-in needed). In this same Terminal, run:"
  echo
  echo "  sudo apt install gh"
  echo "  gh auth login"
  echo
  echo "Choose GitHub.com, HTTPS, and Login with a web browser."
  echo "Then run this send script again."
  exit 1
}

if [[ ! -d "$ROOT/.git" ]]; then
  echo "This folder is not a git copy yet (Hub only linked GitHub; it did not download)."
  echo "Saving your current files, then combining them with GitHub, then uploading."
  if [[ ! -f "$ROOT/.gitignore" ]]; then
    curl -fsSL "https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/.gitignore" -o "$ROOT/.gitignore"
  fi
  git init
  ensure_git_identity
  git checkout -B main >/dev/null
  git add -A
  if git diff --cached --quiet; then
    git commit --allow-empty -m "local work before first GitHub send"
  else
    git commit -m "Local Unity Hub folder $(date -u +%Y-%m-%d)"
  fi
  git remote add origin "$ORIGIN_URL"
  if ! git fetch origin; then
    echo "Could not reach GitHub. Check the network, then run this again."
    exit 1
  fi
  if ! git merge origin/main --allow-unrelated-histories --no-edit; then
    echo
    echo "Stopped. GitHub and your folder both changed the same files."
    echo "Nothing was deleted. Tell the agent: merge conflict in ProjectARGON."
    exit 1
  fi
  push_main
else
  ensure_git_identity
  if ! git remote get-url origin 2>/dev/null | grep -q "ProjectARGON"; then
    echo "This folder's git remote is not GreatSaiyaDave/ProjectARGON."
    echo "Remote is: $(git remote get-url origin 2>/dev/null || echo none)"
    echo "Stopped so we do not upload the wrong project."
    exit 1
  fi
  git checkout -B main >/dev/null
  commit_local_if_needed
  merge_github
  LOCAL="$(git rev-parse HEAD)"
  REMOTE="$(git rev-parse origin/main)"
  if [[ "$LOCAL" == "$REMOTE" ]]; then
    echo "Already synced with GitHub. Nothing new to upload."
    uploaded=0
  else
    push_main
    uploaded=1
  fi
fi

echo
if [[ -f "$ROOT/Assets/WRLDZ_BUILD.txt" ]]; then
  echo "BUILD stamp: $(tr -d '\r' < "$ROOT/Assets/WRLDZ_BUILD.txt")"
  echo "Wanted: CLEAN-0909"
else
  echo "No WRLDZ_BUILD.txt — still an old copy."
fi
if [[ "${uploaded:-1}" -eq 1 ]]; then
  echo "Done. GitHub main now has this folder's saved work (plus GitHub's)."
fi
echo "Keep using this same folder in Unity Hub (do not Add a second copy)."

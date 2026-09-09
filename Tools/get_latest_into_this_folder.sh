#!/usr/bin/env bash
# Pull GitHub main into THIS Unity folder. Does not replace the folder.
# Keeps local work. Stops instead of overwriting.
set -euo pipefail

ROOT="$(pwd)"
if [[ ! -d "$ROOT/Assets" || ! -d "$ROOT/ProjectSettings" ]]; then
  echo "Run this inside your Unity project folder (the one that contains Assets)."
  echo "Now in: $ROOT"
  exit 1
fi

if [[ -f "$ROOT/Temp/UnityLockfile" ]]; then
  echo "Unity is still open. Close Unity, then run this again."
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

if [[ ! -d "$ROOT/.git" ]]; then
  echo "This folder is not a git copy yet (Hub only linked GitHub; it did not download)."
  echo "Saving your current files as a local commit, then merging GitHub on top."
  echo "Your work stays. If files clash, this script stops and does not wipe them."
  if [[ ! -f "$ROOT/.gitignore" ]]; then
    curl -fsSL "https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/.gitignore" -o "$ROOT/.gitignore"
  fi
  git init
  ensure_git_identity
  git add -A
  if git diff --cached --quiet; then
    git commit --allow-empty -m "local work before first GitHub merge"
  else
    git commit -m "local work before first GitHub merge"
  fi
  git remote add origin "$ORIGIN_URL"
  git fetch origin
  git checkout -B main
  if ! git merge origin/main --allow-unrelated-histories --no-edit; then
    echo
    echo "Stopped. GitHub and your folder both changed the same files."
    echo "Nothing was deleted. Tell the agent: merge conflict in ProjectARGON."
    exit 1
  fi
else
  ensure_git_identity
  if ! git remote get-url origin 2>/dev/null | grep -q "ProjectARGON"; then
    echo "This folder's git remote is not GreatSaiyaDave/ProjectARGON."
    echo "Remote is: $(git remote get-url origin 2>/dev/null || echo none)"
    echo "Stopped so we do not pull the wrong project onto your work."
    exit 1
  fi
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "You have unsaved local edits. Stopped so they are not overwritten."
    git status -sb | head -40
    echo
    echo "To SAVE those edits onto GitHub, close Unity and paste:"
    echo "curl -fsSL https://raw.githubusercontent.com/GreatSaiyaDave/ProjectARGON/main/Tools/send_this_folder_to_github.sh | bash"
    echo
    echo "Do not zip-replace this folder."
    exit 1
  fi
  git fetch origin
  if git pull --ff-only origin main; then
    :
  else
    echo "GitHub and this folder both moved. Merging without deleting your commits."
    if ! git merge origin/main --no-edit; then
      echo "Stopped on conflict. Your files are still here."
      exit 1
    fi
  fi
fi

echo
if [[ -f "$ROOT/Assets/WRLDZ_BUILD.txt" ]]; then
  echo "BUILD stamp: $(tr -d '\r' < "$ROOT/Assets/WRLDZ_BUILD.txt")"
  echo "Wanted: CLEAN-0909"
else
  echo "No WRLDZ_BUILD.txt — still an old copy."
fi
echo "Done. Open this same folder in Unity Hub (do not Add a second copy)."

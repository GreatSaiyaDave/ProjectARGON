#!/usr/bin/env bash
# snapshot.sh save <dir> <batch.patch>   |   snapshot.sh restore <dir>
#
# Integration rollback for the real checkout, limited to exactly the files one card batch touches (so a
# restore can never clobber another session's concurrent work elsewhere in the tree):
#   save    — copies every file named in <batch.patch> (git diff format), plus the compiler Version file and
#             the compiled-effects seed, into <dir>; files the patch creates are recorded as absent.
#   restore — puts those files back byte-for-byte and deletes the ones the batch created.
# Save immediately before applying one batch; restore if that batch cannot be made green.
# Never use git stash / reset / checkout in this checkout: other sessions keep uncommitted work in it.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mode="${1:?usage: snapshot.sh save <dir> <batch.patch> | restore <dir>}"
dir="${2:?usage: snapshot.sh save <dir> <batch.patch> | restore <dir>}"
ALWAYS=(Assets/Scripts/WRLDZ/Duel/TextEffects/CardTextEffectCompiler.cs
        Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json)

case "$mode" in
  save)
    patch_file="${3:?usage: snapshot.sh save <dir> <batch.patch>}"
    rm -rf "$dir"
    mkdir -p "$dir/files"
    : > "$dir/.absent"
    : > "$dir/.present"
    {
      printf '%s\n' "${ALWAYS[@]}"
      grep -E '^diff --git a/' "$patch_file" | sed -E 's#^diff --git a/(.*) b/(.*)$#\1\n\2#'
    } | sort -u | while IFS= read -r rel; do
      if [[ -z "$rel" ]]; then continue; fi
      if [[ -e "$REPO/$rel" ]]; then
        mkdir -p "$dir/files/$(dirname "$rel")"
        cp -p "$REPO/$rel" "$dir/files/$rel"
        echo "$rel" >> "$dir/.present"
        # A new .cs file gets a Unity .meta beside it; keep the pair together. A file outside Assets/ (a Tools/
        # script or doc) has no .meta: skip it instead of failing the whole save under `set -e`.
        if [[ -e "$REPO/$rel.meta" ]]; then
          cp -p "$REPO/$rel.meta" "$dir/files/$rel.meta"
          echo "$rel.meta" >> "$dir/.present"
        fi
      else
        echo "$rel" >> "$dir/.absent"
        echo "$rel.meta" >> "$dir/.absent"
      fi
    done
    echo "snapshot saved: $dir ($(wc -l < "$dir/.present") files, $(( $(wc -l < "$dir/.absent") / 2 )) new)"
    ;;
  restore)
    [[ -f "$dir/.present" ]] || { echo "snapshot.sh: $dir is not a snapshot" >&2; exit 1; }
    while IFS= read -r rel; do
      [[ -z "$rel" ]] && continue
      mkdir -p "$REPO/$(dirname "$rel")"
      cp -p "$dir/files/$rel" "$REPO/$rel"
    done < "$dir/.present"
    while IFS= read -r rel; do
      [[ -z "$rel" ]] && continue
      rm -f "$REPO/$rel"
    done < "$dir/.absent"
    echo "snapshot restored: $dir"
    ;;
  *)
    echo "usage: snapshot.sh save <dir> <batch.patch> | restore <dir>" >&2
    exit 2
    ;;
esac

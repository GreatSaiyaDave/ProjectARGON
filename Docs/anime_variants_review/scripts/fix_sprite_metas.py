#!/usr/bin/env python3
"""Repair the malformed anime-sprite .meta files (367 of 403 under AnimeCharacters).

The template used to write them (see the copy embedded in build_normal_depth.py) indents the
`bumpmap:` block — and its siblings `isReadable` / `streamingMipmaps` / `streamingMipmapsPriority` —
one level too deep, so they nest under `mipmaps:`. It also drops the Android/iOS platform entries and
the trailing newline. Unity rejects those files outright:

    The contents of '...yugi_da_front.png.meta' could not be parsed.
    Please ensure that this file is a valid YAML formatted file.

so the textures are never imported and `Resources.Load<Texture2D>("WRLDZ/AnimeCharacters/<set>/<name>")`
returns null. `AnimeCharacterActor.LoadAngleSprite` then bails before assigning `_BaseMap`, the
AnimeCel material keeps its default white texture (whose alpha is 1 everywhere, so the cutout never
clips) and the sprite renders as a flat white rectangle — the owner's "white spot". The form map
fails the same way, so it also loses all shading.

This rewrites every malformed meta from a canonical sibling Unity does accept
(joey / kaiba / yami_yugi are healthy), keeping each file's own GUID so nothing that references an
asset breaks. Normal maps get sRGBTexture 0, alphaIsTransparency 0 and maxTextureSize 512.

    python3 Docs/anime_variants_review/scripts/fix_sprite_metas.py [--check]

`--check` reports what would change without writing.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SPRITES = ROOT / "Assets/Resources/WRLDZ/AnimeCharacters"

# A meta Unity accepts today (checked: parses, GUID present, bumpmap at the TextureImporter level).
CANONICAL = SPRITES / "joey/joey_front.png.meta"

GUID_RE = re.compile(r"^guid: ([0-9a-f]{32})$", re.M)


def healthy(text: str) -> bool:
    return "\n  bumpmap:" in text and text.endswith("\n") and GUID_RE.search(text) is not None


def scalars(text: str) -> dict[str, str]:
    """Leaf `key: value` pairs, ignoring list entries and the guid line."""
    out: dict[str, str] = {}
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("- ") or s.startswith("guid:"):
            continue
        if ": " in s:
            k, v = s.split(": ", 1)
            if k and not k.startswith("#"):
                out.setdefault(k, v)
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="report only, write nothing")
    args = ap.parse_args()

    canonical = CANONICAL.read_text(encoding="utf-8")
    if not healthy(canonical):
        print(f"canonical template is not healthy: {CANONICAL}", file=sys.stderr)
        return 2

    metas = sorted(SPRITES.glob("*/*.png.meta"))
    bad = [p for p in metas if not healthy(p.read_text(encoding="utf-8", errors="replace"))]
    print(f"metas {len(metas)} · malformed {len(bad)}")

    ref = scalars(canonical)
    other_diffs: dict[str, set[str]] = {}
    written = 0
    for p in bad:
        text = p.read_text(encoding="utf-8", errors="replace")
        m = GUID_RE.search(text)
        if m is None:
            print(f"  !! no guid in {p.relative_to(ROOT)} — skipped", file=sys.stderr)
            continue
        guid = m.group(1)

        # Anything that differs from the canonical beyond the known defects is worth knowing about.
        cur = scalars(text)
        for k, v in cur.items():
            if k in ref and ref[k] != v:
                other_diffs.setdefault(k, set()).add(f"{v} -> {ref[k]}")

        out = GUID_RE.sub(f"guid: {guid}", canonical, count=1)
        if p.name.endswith("_normal.png.meta"):
            out = out.replace("sRGBTexture: 1", "sRGBTexture: 0")
            out = out.replace("alphaIsTransparency: 1", "alphaIsTransparency: 0")
            out = out.replace("maxTextureSize: 1024", "maxTextureSize: 512")
        if not args.check:
            p.write_text(out, encoding="utf-8")
        written += 1

    for k, vals in sorted(other_diffs.items()):
        print(f"  value differs from canonical · {k}: {', '.join(sorted(vals))}")
    print(f"{'would write' if args.check else 'rewrote'} {written} metas")

    # ---- verify
    bad_after = [p for p in metas if not healthy(p.read_text(encoding="utf-8", errors="replace"))]
    guids = [GUID_RE.search(p.read_text(encoding="utf-8")).group(1) for p in metas]
    print(f"malformed after: {len(bad_after)} · unique guids: {len(set(guids))}/{len(guids)}")
    return 0 if not bad_after and len(set(guids)) == len(guids) else 1


if __name__ == "__main__":
    raise SystemExit(main())

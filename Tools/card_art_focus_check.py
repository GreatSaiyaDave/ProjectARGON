#!/usr/bin/env python3
"""Guards CardArtFocus.ArtworkNormRect, the illustration crop on every arena holo.

Exit 1 if the rect's edge lands on the card frame or the bevel around the art
box, the crop stops matching the square monster holo quad, or the full-card /
pre-cropped guards that keep square packs (Kuriboh 624x624) uncropped go missing.

Frame test (needs Pillow): the frame and bevel look the same on every scan of a
frame type, and the illustration differs from card to card. A colour test can't
separate them: Weather Report's orange sky matches the orange Effect frame. So
for a sample of each frame type, measure how much each texel varies across
cards. Every rect edge (the outermost texel bilinear filtering reads, plus one
texel of margin) must vary as much as the middle of the art. The rect used
before 2026-09-23 showed the SPELL CARD strip and side frame, so it must still fail.
"""
from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FOCUS = ROOT / "Assets/Scripts/WRLDZ/Presentation/CardArtFocus.cs"
DB_CS = ROOT / "Assets/Scripts/WRLDZ/Data/CardDatabase.cs"
DB = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
ART = ROOT / "Assets/StreamingAssets/CardArt"

SCAN = (268, 391)  # the full-card scan size; one pixel grid for the cross-card test
SAMPLE = 96  # scans per frame type
MIN_TYPE_SAMPLE = 8
MIN_ART = 0.7  # edge variation / art-middle variation: art ~0.9-1.0, half-bevel texel ~0.6, frame ~0.1
MAX_ASPECT_DRIFT = 0.02
OLD_RECT = (0.09, 0.30, 0.82, 0.55)

FLOAT = r"([0-9.]+)f"


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def main() -> int:
    for p in (FOCUS, DB_CS, DB):
        if not p.is_file():
            fail(f"missing {p.relative_to(ROOT)}")

    src = FOCUS.read_text(encoding="utf-8")
    m = re.search(rf"ArtworkNormRect = new\({FLOAT}, {FLOAT}, {FLOAT}, {FLOAT}\)", src)
    if m is None:
        fail("could not parse CardArtFocus.ArtworkNormRect")
    rect = tuple(map(float, m.groups()))
    x, y, w, h = rect
    if min(rect) < 0 or x + w > 1 or y + h > 1:
        fail(f"ArtworkNormRect {rect} leaves the texture")

    # Monster holo quads use MonsterArtworkScale; a mismatched crop stretches the art.
    m = re.search(rf"MonsterArtworkScale = new\({FLOAT}, {FLOAT}, 1f\)", src)
    if m is None:
        fail("could not parse CardArtFocus.MonsterArtworkScale")
    quad = float(m.group(1)) / float(m.group(2))
    crop = (w * SCAN[0]) / (h * SCAN[1])
    if abs(crop / quad - 1) > MAX_ASPECT_DRIFT:
        fail(f"crop is {crop:.3f}:1 on a {SCAN[0]}x{SCAN[1]} scan but the monster holo quad is {quad:.3f}:1")

    # Square packs must never be re-cropped (double crop).
    if "cropToArtwork && LooksLikeFullCardScan(tex) && !IsPreCroppedIllustration(tex)" not in src:
        fail("CardArtFocus.ApplyToMaterial lost its full-card / pre-cropped guard")
    db_cs = DB_CS.read_text(encoding="utf-8")
    if db_cs.count("IsPreCroppedIllustration(") < 2 or db_cs.count("LooksLikeFullCardScan(") < 2:
        fail("CardDatabase.GetArtwork lost its full-card / pre-cropped guards")
    full = re.search(rf"return a > {FLOAT} && a < {FLOAT};", src)
    square = re.search(rf"return a >= {FLOAT};", src)
    if full is None or square is None:
        fail("could not parse the LooksLikeFullCardScan / IsPreCroppedIllustration thresholds")
    full_lo, full_hi = map(float, full.groups())
    square_lo = float(square.group(1))

    art_note = check_rect_on_art(rect, full_lo, full_hi, square_lo)

    print("CardArtFocus illustration crop")
    print(f"  ArtworkNormRect {rect} · {w * SCAN[0]:.0f}x{h * SCAN[1]:.0f} texels, "
          f"{crop:.3f}:1 vs holo quad {quad:.3f}:1")
    print("  full-card / pre-cropped guards present in ApplyToMaterial and GetArtwork")
    print(f"  frame test: {art_note}")
    print("PASS")
    return 0


def edge_texels(rect: tuple[float, ...], margin: int) -> dict[str, list[tuple[int, int]]]:
    """Outermost texels a bilinear sample at the rect edge reads, pushed out by margin.

    Unity rects are bottom-left origin; PIL rows run top-down.
    """
    x, y, w, h = rect
    sw, sh = SCAN
    left = math.floor(x * sw - 0.5) - margin
    right = math.ceil((x + w) * sw - 0.5) + margin
    top = math.floor((1 - y - h) * sh - 0.5) - margin
    bottom = math.ceil((1 - y) * sh - 0.5) + margin
    return {
        "left": [(left, j) for j in range(top, bottom + 1)],
        "right": [(right, j) for j in range(top, bottom + 1)],
        "top": [(i, top) for i in range(left, right + 1)],
        "bottom": [(i, bottom) for i in range(left, right + 1)],
    }


def check_rect_on_art(rect: tuple[float, ...], full_lo: float, full_hi: float, square_lo: float) -> str:
    try:
        from PIL import Image  # optional on agent VMs
    except ImportError:
        return "skipped (Pillow not installed)"

    cards = json.loads(DB.read_text(encoding="utf-8"))["cards"]
    frame_of = {str(c["id"]): c.get("frameType") for c in cards}

    by_type: dict[str, list[Path]] = {}
    unsampled = 0  # other sizes, alt-art ids with no frame type, or neither shape (never cropped)
    squares = []
    for path in sorted(ART.glob("*.jpg")):
        with Image.open(path) as im:
            size = im.size
        a = size[0] / size[1]
        if a >= square_lo:
            squares.append(path.stem)
            continue
        ftype = frame_of.get(path.stem)
        if size != SCAN or not ftype or not full_lo < a < full_hi:
            unsampled += 1
            continue
        by_type.setdefault(ftype, []).append(path)
    if not squares:
        fail("no square illustration pack left in CardArt to prove the no-double-crop guard")

    new_edges = [edge_texels(rect, m) for m in (0, 1)]
    old_edges = edge_texels(OLD_RECT, 0)
    # Middle of the art: the central 40% of the rect, every third texel.
    x, y, w, h = rect
    cx0, cx1 = int((x + 0.3 * w) * SCAN[0]), int((x + 0.7 * w) * SCAN[0])
    cy0, cy1 = int((1 - y - 0.7 * h) * SCAN[1]), int((1 - y - 0.3 * h) * SCAN[1])
    core = [(i, j) for i in range(cx0, cx1, 3) for j in range(cy0, cy1, 3)]
    texels = set(core)
    for edges in new_edges + [old_edges]:
        for line in edges.values():
            texels.update(line)

    lines = []
    worst = math.inf
    for ftype, paths in sorted(by_type.items()):
        if len(paths) < MIN_TYPE_SAMPLE:
            continue
        step = max(1, len(paths) // SAMPLE)
        sample = paths[::step][:SAMPLE]
        acc = {t: [0.0] * 6 for t in texels}
        for path in sample:
            with Image.open(path) as im:
                px = im.convert("RGB").load()
            for t in texels:
                p = px[t]
                s = acc[t]
                for c in range(3):
                    s[c] += p[c]
                    s[c + 3] += p[c] * p[c]
        n = len(sample)

        def spread(ts: list[tuple[int, int]]) -> float:
            total = 0.0
            for t in ts:
                s = acc[t]
                total += sum(math.sqrt(max(0.0, s[c + 3] / n - (s[c] / n) ** 2)) for c in range(3)) / 3
            return total / len(ts)

        art = spread(core)
        for edges in new_edges:
            for side, line in edges.items():
                ratio = spread(line) / art
                worst = min(worst, ratio)
                if ratio < MIN_ART:
                    fail(f"ArtworkNormRect {side} edge sits on the {ftype} frame or bevel "
                         f"(varies {ratio:.2f}x the art across {n} scans, need {MIN_ART})")
        if min(spread(line) / art for line in old_edges.values()) >= MIN_ART:
            fail(f"the old rect {OLD_RECT} passed on {ftype}; the frame test lost its teeth")
        lines.append(f"{ftype} {n}")

    if not lines:
        fail(f"no frame type has {MIN_TYPE_SAMPLE}+ {SCAN[0]}x{SCAN[1]} scans to sample")
    note = (f"edges clear of frame and bevel ({', '.join(lines)} scans; weakest edge {worst:.2f}x "
            f"the art, old rect caught) · {len(squares)} square pack(s) left uncropped")
    if unsampled:
        note += f" · {unsampled} scan(s) not sampled (other size, no frame type or not full-card)"
    return note


if __name__ == "__main__":
    raise SystemExit(main())

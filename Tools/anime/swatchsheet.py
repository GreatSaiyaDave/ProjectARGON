#!/usr/bin/env python3
"""swatchsheet.py — contact sheet from the Phase 1 audit (out/canon_texture_audit.json).

Renders one row per canon look: the CURRENT hair zone swatches, the MEASURED target zone swatches,
the measured shade, and the verdict — so the owner / a vision model can sign off the deltas
before any texture is repainted. Needs only PIL (meas.py already pulls it in).

    Tools/anime/animepy swatchsheet.py            # -> out/phase1_hair_swatches.png
"""
import json
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")


def hexrgb(h):
    h = (h or "#000000").lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def swatch(d, x, y, w, h, hexcol, label="", font=None):
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=hexrgb(hexcol), outline=(255, 255, 255))
    if label and font:
        d.text((x + 3, y + (h - 14) // 2), label, fill=(20, 20, 20), font=font)


def main():
    rep = json.load(open(os.path.join(OUT, "canon_texture_audit.json"), encoding="utf-8"))
    # fix the order: measure rows first, then unmeasured
    hair = rep["hair"]
    measured = [e for e in hair if "target_lit" in e]
    unmeasured = [e for e in hair if "target_lit" not in e]

    font = ImageFont.load_default()
    zone_labels = ("root", "tip", "fringe", "streak")

    sw = 64           # swatch width
    sh = 26           # swatch height
    padx, pady = 14, 10
    colx = padx
    row_h = 40
    top = 42
    text_w = 240
    arrow_w = 26
    pair_w = sw + 4 + 16 + sw + 4 + 74   # one cur→tgt pair + Δ note column

    rows = []
    for e in measured:
        roles = list(e["target_lit"])
        labels = [f"{e['look']}  {e['measured_char']}  [{e['verdict']}]"]
        pairs = []
        for z in roles:
            cur = (e["current"] or {}).get(z)
            tgt = e["target_lit"].get(z)
            d = (e["delta"] or {}).get(z)
            ds = f"{d['max']}" if d else "-"
            label = "Δ" + ds if cur and tgt else ("new zone" if not cur and tgt else "")
            pairs.append((z, cur, tgt, label))
        rows.append(({"label": labels[0], "pairs": pairs}, True))
    for e in unmeasured:
        rows.append(({"label": f"{e['look']}  —  {e['note']}", "pairs": []}, False))

    # width = text + arrow + max zones*(pair width)
    max_zones = max((len(r[0]["pairs"]) for r in rows), default=0)
    W = text_w + arrow_w + max_zones * pair_w + padx * 2
    H = top + pady * 2 + len(rows) * row_h

    img = Image.new("RGB", (W, H), (18, 18, 24))
    d = ImageDraw.Draw(img)
    d.text((padx, 6), "Phase 1 hair swatches — current (code) vs measured (anime frame)", fill=(235, 235, 240), font=font)
    d.text((padx, H - 16), "swatch = hex from Tools/anime/out/canon_texture_audit.json (MEASUREMENT_NOTES.md citations)",
           fill=(150, 150, 160), font=font)

    y = top
    for (row, has_measured) in rows:
        d.text((padx, y + 4), row["label"], fill=(235, 235, 240), font=font)
        x = padx + text_w
        for z, cur, tgt, note in row["pairs"]:
            d.text((x, y - 14), z, fill=(140, 140, 160), font=font)
            swatch(d, x, y, sw, sh, cur or "#333333")
            x += sw + 4
            d.text((x, y + sh // 2 - 8), "→" if cur else "+", fill=(235, 235, 240), font=font)
            x += 16
            swatch(d, x, y, sw, sh, tgt)
            x += sw + 4
            d.text((x, y + 4), note, fill=(255, 200, 90) if note else (120, 120, 130), font=font)
            x += 74                                    # Δ note column width
        y += row_h

    os.makedirs(OUT, exist_ok=True)
    out = os.path.join(OUT, "phase1_hair_swatches.png")
    img.save(out)
    print(f"wrote {out}  ({W}x{H})")


if __name__ == "__main__":
    main()
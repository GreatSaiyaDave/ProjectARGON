"""repaint_hair.py — Phase 2: repaint the canon Original-mode hair textures to the anime measurements.

For every canon duelist look whose hair pattern is Original (0), the shown colour IS the painted
Hair_<id>.png (WRLDZ/AvatarHair shader: when _Original=1 the texture is drawn as-is). So "exact canon
repaint" for those styles = re-colouring the texture, NOT a code hex. Styles with pattern != Original
are driven by _Hair* material hexes from CharacterLooks.cs and are handled separately (edit rows only).

The recolor is the same luminance-rank method as ~/argon-avatar/tools/custom/hair_recolor.py ("relative"
mode): texels inside a class are sorted by luminance, the median texel lands at `median_at` on the ramp
and the p10..p90 spread maps to +-spread/2 — the painted light / shade survives, re-anchored onto the
measured stops. Every stop below is a measured value from measured/hair_summary.json (lit / shade / zone).

Class masks (hue/sat/val windows) come from the observed pixel distribution of the current atlases
(see Tools/anime/MEASUREMENT_NOTES.md Phase 2 / audit run: yugi_muto shell magenta 330-360°, bangs gold
30-60°; mono styles red-brown hues 0-70° with dark-green background remnants 90-200° to exclude; ryou_bakura
is a bright low-sat silver with green remnants).

  Tools/anime/animepy repaint_hair.py            # repaints + writes out/hair_before/ + out/hair_repaint_evidence.md + .png
"""
import json
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
HAIR = os.path.join(ROOT, "Assets", "Resources", "WRLDZ", "Avatar3D", "Hair")
OUT = os.path.join(HERE, "out")
BEFORE = os.path.join(OUT, "hair_before")


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64) / 255.0


def srgb_to_lin(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def ramp_blend(stops, t):
    tsa = np.asarray([s[0] for s in stops])
    cols = np.asarray([s[1] for s in stops])
    t = np.clip(t, 0.0, 1.0)
    i = np.clip(np.searchsorted(tsa, t, side="right") - 1, 0, len(stops) - 2).astype(int)
    t0, t1 = tsa[i], tsa[i + 1]
    w = np.clip((t - t0) / np.maximum(t1 - t0, 1e-9), 0.0, 1.0)
    return cols[i] * (1 - w[..., None]) + cols[i + 1] * w[..., None]


def hsv_of(rgb):
    mx = rgb.max(1)
    mn = rgb.min(1)
    d = mx - mn
    sat = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0.0)
    hue = np.zeros_like(mx)
    nz = d > 1e-6
    rr = rgb[nz, 0]
    gg = rgb[nz, 1]
    bb = rgb[nz, 2]
    mxx = mx[nz]
    dd = d[nz]
    h = np.where(mxx == rr, ((gg - bb) / dd) % 6,
                 np.where(mxx == gg, (bb - rr) / dd + 2, (rr - gg) / dd + 4))
    hue[nz] = (h / 6.0 * 360.0) % 360.0
    return hue, sat, mx


def in_hue(h, windows):
    ok = np.zeros(len(h), dtype=bool)
    for lo, hi in windows:
        ok |= (h >= lo) & (h < hi)
    if any(lo < 0 for lo, _ in windows):
        pass
    return ok


CLASSES = {
    "yugi_muto": [
        {"name": "shell", "hue": ((300, 360), (0, 20)), "sat_min": 0.2, "val_min": 0.06,
         "ramp": ((0.0, hexrgb("#131212")), (1.0, hexrgb("#9C1843"))),
         "median_at": 0.55, "spread": 0.9},
        {"name": "bangs", "hue": ((20, 95),), "sat_min": 0.15, "val_min": 0.25,
         "ramp": ((0.0, hexrgb("#836A2E")), (1.0, hexrgb("#C0B84E"))),
         "median_at": 0.6, "spread": 0.8},
    ],
    "seto_kaiba": [
        {"name": "hair", "hue": ((0, 70), (300, 360)), "sat_min": 0.03, "val_min": 0.05,
         "ramp": ((0.0, hexrgb("#58442F")), (1.0, hexrgb("#765A3D"))),
         "median_at": 0.85, "spread": 0.8},
    ],
    "joey_wheeler": [
        {"name": "hair", "hue": ((0, 70), (300, 360)), "sat_min": 0.03, "val_min": 0.05,
         "ramp": ((0.0, hexrgb("#7E6A3E")), (1.0, hexrgb("#B69D3D"))),
         "median_at": 0.85, "spread": 0.8},
    ],
    "mai_valentine": [
        {"name": "hair", "hue": ((0, 70), (300, 360)), "sat_min": 0.03, "val_min": 0.05,
         "ramp": ((0.0, hexrgb("#736C41")), (1.0, hexrgb("#C2BD6A"))),
         "median_at": 0.85, "spread": 0.8},
    ],
    "tea_gardner": [
        {"name": "hair", "hue": ((0, 70), (300, 360)), "sat_min": 0.03, "val_min": 0.05,
         "ramp": ((0.0, hexrgb("#2A1B13")), (1.0, hexrgb("#5D2F20"))),
         "median_at": 0.85, "spread": 0.8},
    ],
    "ryou_bakura": [
        {"name": "hair", "sat_max": 0.2, "val_min": 0.45,
         "ramp": ((0.0, hexrgb("#AFA8B6")), (1.0, hexrgb("#D7D8DF"))),
         "median_at": 0.85, "spread": 0.8},
    ],
}

TARGETS = {
    "yugi_muto": ("#9C1843", "#C0B84E"),
    "seto_kaiba": ("#765A3D",),
    "joey_wheeler": ("#B69D3D",),
    "mai_valentine": ("#C2BD6A",),
    "tea_gardner": ("#5D2F20",),
    "ryou_bakura": ("#D7D8DF",),
}


def class_mask(rgb, cls):
    hue, sat, val = hsv_of(rgb)
    m = np.ones(len(rgb), dtype=bool)
    if "hue" in cls:
        m &= in_hue(hue, cls["hue"])
    if cls.get("sat_min") is not None:
        m &= sat >= cls["sat_min"]
    if cls.get("sat_max") is not None:
        m &= sat <= cls["sat_max"]
    if cls.get("val_min") is not None:
        m &= val >= cls["val_min"]
    if cls.get("val_max") is not None:
        m &= val <= cls["val_max"]
    return m


def recolor(im, classes):
    a = np.asarray(im).astype(np.float64) / 255.0
    rgb = a[..., :3].reshape(-1, 3)
    out = rgb.copy()
    stats = []
    for cls in classes:
        m = class_mask(rgb, cls)
        if m.sum() < 32:
            stats.append(dict(name=cls["name"], n=int(m.sum()), before=None, after=None))
            continue
        lum = srgb_to_lin(rgb[m]).dot(np.array([0.2126, 0.7152, 0.0722]))
        order = np.argsort(lum, kind="stable")
        ranks = np.empty(lum.shape[0])
        ranks[order] = np.arange(lum.shape[0]) / max(lum.shape[0] - 1, 1)
        tgt = cls["median_at"] + (ranks - 0.5) * cls["spread"]
        new = ramp_blend(cls["ramp"], tgt)
        out[m] = new
        stats.append(dict(name=cls["name"], n=int(m.sum()),
                          before=rgb[m].mean(0), after=out[m].mean(0),
                          after_med=np.median(out[m], 0), after_p90=p90_lum(out[m])))
    out = out.reshape(a.shape[0], a.shape[1], 3)
    res = a.copy()
    res[..., :3] = out
    res = np.clip(res, 0.0, 1.0)
    return (res * 255.0).round().astype(np.uint8), stats


def hex_of(arr):
    return "#%02X%02X%02X" % tuple(int(round(c * 255)) for c in arr)


def p90_lum(px):
    lum = srgb_to_lin(px).dot(np.array([0.2126, 0.7152, 0.0722]))
    cut = int(np.ceil(px.shape[0] * 0.1))
    idx = np.argsort(lum, kind="stable")[-cut:]
    return px[idx].mean(0)


def main():
    os.makedirs(BEFORE, exist_ok=True)
    lines = []
    sheet = []
    for style, classes in CLASSES.items():
        p = os.path.join(HAIR, f"Hair_{style}.png")
        im = Image.open(p).convert("RGBA")
        before_im = im.copy()
        before_im.save(os.path.join(BEFORE, f"Hair_{style}.png"))
        new, stats = recolor(im, classes)
        Image.fromarray(new, "RGBA").save(p)
        lines.append(f"## {style}  ->  {os.path.basename(p)}")
        for s in stats:
            b = "#----" if s["before"] is None else hex_of(s["before"])
            a = hex_of(s["after"])
            med = hex_of(s["after_med"])
            p90 = hex_of(s["after_p90"])
            lines.append(f"- {s['name']}: n={s['n']}  before-mean {b}  ->  after-mean {a}  (median {med}, p90 {p90})")
        lines.append("")
        sheet.append((style, before_im, Image.fromarray(new, "RGBA")))
        print(f"{style:14} classed {sum(s['n'] for s in stats):>8} px -> {os.path.basename(p)}")

    md = ["# Hair texture repaint evidence (Phase 2)", "",
          "Method: luminance-rank 'relative' recolor (same algorithm as ~/argon-avatar/tools/custom/hair_recolor.py); ",
          "every ramp stop is a measured value from `Tools/anime/measured/hair_summary.json` (MEASUREMENT_NOTES.md).",
          "Original-mode canon looks show the texture as-is, so this IS the canon colour.", ""] + lines
    with open(os.path.join(OUT, "hair_repaint_evidence.md"), "w") as fh:
        fh.write("\n".join(md) + "\n")

    TW = 256
    cols = len(sheet)
    W = cols * (TW + 16) + 16
    H = 2 * (TW + 52) + 16
    cs = Image.new("RGB", (W, H), (20, 20, 26))
    from PIL import ImageDraw
    d = ImageDraw.Draw(cs)
    for i, (style, before, after) in enumerate(sheet):
        x = 16 + i * (TW + 16)
        d.text((x, 6), style, fill=(235, 235, 240))
        cs.paste(before.convert("RGB").resize((TW, TW)), (x, 22))
        d.text((x, TW + 26), "after", fill=(235, 235, 240))
        cs.paste(after.convert("RGB").resize((TW, TW)), (x, TW + 32))
    csp = os.path.join(OUT, "phase2_hair_repaint_sheet.png")
    cs.save(csp)
    print(f"wrote {csp}")


if __name__ == "__main__":
    main()

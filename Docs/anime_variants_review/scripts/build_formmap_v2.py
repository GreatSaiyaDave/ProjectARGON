#!/usr/bin/env python3
"""
Form map v2 — real body roundness instead of a silhouette pillow.

Why v2: build_normal_depth.py derives depth from the Euclidean distance transform of the
silhouette, normalised by the figure's widest point. That field is almost FLAT across the
body (its gradient only spikes within a few pixels of the edge), so the generated normals
carry no interior curvature. Measured against the AnimeCelShader maths, that made the sprite
degenerate: 0% of the figure in shadow under a frontal/overhead key, 100% under a backlight,
never a terminator crossing the body — i.e. flat either way.

v2 builds the normal from the body's local RUN LENGTHS, which is what actually makes a
figure read as round:

  horizontal: for each pixel, its position WITHIN its own run of the silhouette,
              f = (L-1)/(n-1) in [0,1] -> u = 1 - 2f in [-1, 1] -> nx = -sin(u * PHI)
              Each blob gets its own cylinder, so torso, arms and legs are each rounded,
              and nx keeps growing across the whole body instead of dying at the ridge.
  vertical:   same along the column -> ny = +sin(v * PHI) * NY_SCALE
              crown tilts up, feet tilts down, torso stays ~0.
  depth:      ellipsoidal mass = sqrt(1-u^2) * sqrt(1-v^2)  (parallax channel)

PHI = 1.40 rad caps the silhouette tilt at ~49 deg — enough for the terminator to land
mid-body under a lateral key, deliberately NOT 90 deg so the edge never goes dark and
reads as an ink outline (the art direction forbids outlines).

Writes <id>_normal.png (rgb = tangent normal, a = depth) + a preview sheet per id.

Usage: python3 build_formmap_v2.py [id ...]     (default: every *_da set)
"""
import os, sys, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
DOCS = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Docs/anime_variants_review/normals"

PHI = 1.40         # max silhouette tilt (rad) -> ~49 deg. Swept 1.25..1.70: this is where the
                   # shadow band lands at ~1/3 of the figure with the terminator crossing mid-body,
                   # while the silhouette itself stays lit (no dark rim -> no ink-outline read).
NY_SCALE = 0.75   # keep the vertical (crown/feet) contribution gentle
NORMAL_SIZE = 512


def _left_run(b):
    """Cells from the start of the run (inclusive) to this cell, 0 where b is False."""
    n = b.shape[1]
    i = np.arange(n)
    # index of the nearest False at or BEFORE i (max-accumulate of False positions)
    last_false = np.where(b, -1, i)
    np.maximum.accumulate(last_false, axis=1, out=last_false)
    return np.where(b, i - last_false, 0)


def runs(mask, axis):
    """(left, right) run length INCLUDING self, along axis (1 = rows, 0 = columns).

    A run is a maximal stretch of silhouette in one row/column; every pixel gets the
    extents of ITS OWN run, so separate blobs in the same row (torso vs. outstretched
    arm) each become their own cylinder instead of one figure-wide one.
    """
    b = mask if axis == 1 else mask.T
    L = _left_run(b)
    R = _left_run(b[:, ::-1])[:, ::-1]
    if axis == 1:
        return L, R
    return L.T, R.T


def span_u(mask, axis):
    """Normalised cross-body position in [-1, 1]: +1 at the left edge, -1 at the right."""
    L, R = runs(mask, axis)
    n = L + R - 1
    inner = np.maximum(n - 1, 1)
    pos = 1.0 - 2.0 * np.where(n > 0, (L - 1) / inner, 0.0)
    return pos


def form_map(sprite_rgba):
    """Return (normal_rgba uint8, depth float)."""
    m = sprite_rgba[:, :, 3] > 128
    if not m.any():
        raise SystemExit("empty alpha")

    u = span_u(m, 1)                         # horizontal (per blob -> its own cylinder)
    nx = -np.sin(np.clip(u, -1, 1) * PHI)

    v = span_u(m, 0)                         # vertical: crown tilts up, feet tilt down
    ny = np.sin(np.clip(v, -1, 1) * PHI) * NY_SCALE

    depth = np.sqrt(np.clip(1 - u * u, 0, 1)) * np.sqrt(np.clip(1 - v * v, 0, 1))

    nx = np.where(m, np.clip(nx, -0.98, 0.98), 0.0)
    ny = np.where(m, np.clip(ny, -0.98, 0.98), 0.0)
    depth = np.where(m, depth, 0.0)

    rgb = np.dstack([(nx * 0.5 + 0.5) * 255, (ny * 0.5 + 0.5) * 255,
                     np.full_like(nx, 255.0)]).astype(np.uint8)
    out = np.dstack([rgb, (depth * 255).astype(np.uint8)])
    out[~m] = (128, 128, 255, 0)
    return out, depth


# --- verification: reproduce AnimeCelShader's shading maths offline -------------

def smoothstep(t0, t1, x):
    t = np.clip((x - t0) / (t1 - t0), 0, 1)
    return t * t * (3 - 2 * t)


PORTRAIT = np.array([-0.88, 0.40, 0.30], np.float32)   # AnimeSpriteKeyDir's fixed key (camera-left)
SCENE_BIAS = 0.15                                       # _KeyBias on the *_da sets in AnimeCharacterActor


def _n(a):
    a = np.array(a, np.float32)
    return a / np.linalg.norm(a)


def shipped(scene):
    """The key the game actually uses for DK: portrait blended toward the scene light."""
    return _n((1.0 - SCENE_BIAS) * _n(PORTRAIT) + SCENE_BIAS * _n(scene))


SCENE_LIGHTS = {"frontal": (0, 0.25, 1), "overhead": (0, 1, 0.45), "right": (0.81, 0.43, 0.41),
                "backlit": (0.15, 0.90, -0.60), "sunset": (-0.6, 0.3, 0.7)}

KEYS = {f"shipped·{k}": shipped(v) for k, v in SCENE_LIGHTS.items()}
KEYS["portrait only"] = _n(PORTRAIT)
KEYS["old·frontal"] = _n(SCENE_LIGHTS["frontal"])
KEYS["old·sunset"] = _n(SCENE_LIGHTS["sunset"])


def evaluate(sprite_rgba, n_rgba, strength):
    """Report the lit/shadow split for each key. Healthy = a partial split on some key."""
    m = sprite_rgba[:, :, 3] > 128
    h, w = m.shape
    n = np.array(Image.fromarray(n_rgba).resize((w, h), Image.BILINEAR)).astype(np.float32) / 255.0 * 2 - 1
    nt = np.stack([n[..., 0] * strength, n[..., 1] * strength, np.ones((h, w))], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)

    from scipy.ndimage import distance_transform_edt
    dist = distance_transform_edt(m)
    interior = m & (dist > 6)

    rows = []
    for name, l in KEYS.items():
        l = np.array(l, np.float32); l /= np.linalg.norm(l)
        ndl = (nt[..., 0] * l[0] + nt[..., 1] * l[1] + nt[..., 2] * l[2]) * 0.5 + 0.5
        lit = smoothstep(0.43, 0.47, ndl)
        shadow = m & (lit < 0.5)
        frac = shadow.sum() / max(m.sum(), 1) * 100
        inner = (shadow & interior).sum() / max(interior.sum(), 1) * 100
        rows.append((name, frac, inner))
    return rows


def preview(cid, sprite, n_rgba):
    """original | normal map | lit with the OLD pure-scene key | lit with the shipped key."""
    os.makedirs(DOCS, exist_ok=True)
    m = sprite[:, :, 3] > 128
    h, w = m.shape
    n = np.array(Image.fromarray(n_rgba).resize((w, h), Image.BILINEAR)).astype(np.float32) / 255.0 * 2 - 1
    nt = np.stack([n[..., 0] * 1.0, n[..., 1] * 1.0, np.ones((h, w))], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
    col = sprite[:, :, :3].astype(np.float32)
    shadow_col = col * np.array([0.80, 0.78, 0.92], np.float32) * 0.92
    tiles = [sprite, n_rgba]
    for key in (_n(SCENE_LIGHTS["frontal"]), shipped(SCENE_LIGHTS["frontal"])):
        ndl = (nt[..., 0] * key[0] + nt[..., 1] * key[1] + nt[..., 2] * key[2]) * 0.5 + 0.5
        lit = smoothstep(0.43, 0.47, ndl)
        out = col * lit[..., None] + shadow_col * (1 - lit[..., None])
        tiles.append(np.dstack([out, sprite[..., 3:4]]))

    tile = 340
    sheet = Image.new("RGB", (tile * len(tiles), tile + 34), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    d.text((8, 8), f"{cid}  —  form map v2 (original · normal map · OLD scene key · shipped key)",
           fill=(235, 235, 245), font=ImageFont.truetype(
               "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 16) if os.path.exists(
               "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf") else ImageFont.load_default())
    for i, t in enumerate(tiles):
        im = Image.fromarray(t.astype(np.uint8), "RGBA")
        im.thumbnail((tile - 8, tile - 8))
        bg = Image.new("RGBA", im.size, (58, 58, 70, 255)); bg.alpha_composite(im)
        sheet.paste(bg.convert("RGB"), (i * tile + 4, 34))
    sheet.save(os.path.join(DOCS, cid + ".jpg"), quality=90)


def main():
    ids = sys.argv[1:] or sorted(d for d in os.listdir(OUT) if d.endswith("_da") and os.path.isdir(os.path.join(OUT, d)))
    report = {}
    for cid in ids:
        p = os.path.join(OUT, cid, f"{cid}_front.png")
        if not os.path.exists(p):
            print("skip", cid); continue
        sprite = np.array(Image.open(p).convert("RGBA"))
        n_rgba, depth = form_map(sprite)
        nimg = Image.fromarray(n_rgba).resize((NORMAL_SIZE, NORMAL_SIZE), Image.LANCZOS)
        nimg.save(os.path.join(OUT, cid, f"{cid}_normal.png"), optimize=True)

        rows = evaluate(sprite, n_rgba, strength=1.0)
        report[cid] = [{"key": k, "shadow_pct": round(s, 1), "interior_shadow_pct": round(i, 1)}
                       for k, s, i in rows]
        preview(cid, sprite, n_rgba)
        print(f"{cid:16} " + "  ".join(f"{k}:{s:.0f}%" for k, s, _ in rows))
    out = os.path.join(DOCS, "formmap_v2_report.json")
    json.dump(report, open(out, "w"), indent=1)
    print("report:", out)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
QC replay of AnimeCelShader — the check that found the billboard orientation bug.

Everything the fragment shader does can be reproduced offline, in ONE frame:

    +x = tangent (mesh local +X, i.e. the direction uv.u runs = image right)
    +y = bitangent (mesh local +Y = up)
    +z = mesh normal  <-- this is the sign that was wrong

The shader builds its key in WORLD space (`AnimeSpriteKeyDir`) and then dots it against
`nWS = T*nx + B*ny + N*nz`, so the effective key in the authored frame is simply
(l.T, l.B, l.N). `AnimeCharacterActor` used to leave the quad at identity, and since Unity's
built-in Quad faces -Z while `Billboard()` points +Z at the camera, the game was drawing the
quad's BACK: art mirror-flipped, and N pointing away from the viewer. That flips the x and z
components of every key, which is the difference between column "A" and column "B" below.

Run:  python3 qc_keyframe.py          (all *_da sets, shipped 512px maps, on disk)
Writes normals/qc_keyframe.json.
"""
import sys, os, json
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_formmap_v2 as B

OUT, DOCS = B.OUT, B.DOCS


def load(cid):
    d = os.path.join(OUT, cid)
    sprite = np.array(Image.open(os.path.join(d, f"{cid}_front.png")).convert("RGBA"))
    h, w = sprite.shape[:2]
    n = np.array(Image.open(os.path.join(d, f"{cid}_normal.png"))     # shipped 512x512
                 .resize((w, h), Image.BILINEAR)).astype(np.float32) / 255.0 * 2 - 1
    return sprite, n


def shade(sprite, n, key, strength=1.0):
    """AnimeShadeKey's maths verbatim: half-lambert + smoothstep(0.43, 0.47)."""
    m = sprite[:, :, 3] > 128
    nt = np.stack([n[..., 0] * strength, n[..., 1] * strength, np.ones(m.shape, np.float32)], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
    l = np.array(key, np.float32); l /= np.linalg.norm(l)
    ndl = (nt[..., 0] * l[0] + nt[..., 1] * l[1] + nt[..., 2] * l[2]) * 0.5 + 0.5
    lit = B.smoothstep(0.43, 0.47, ndl)
    dist = distance_transform_edt(m)
    interior = m & (dist > 6)
    sh = m & (lit < 0.5)
    cols = np.where(m.any(0))[0]
    lo, hi = cols[0], cols[-1]
    thirds = [100.0 * (sh[:, lo + int((hi - lo) * i / 3):lo + int((hi - lo) * (i + 1) / 3)] &
                       m[:, lo + int((hi - lo) * i / 3):lo + int((hi - lo) * (i + 1) / 3)]).sum() /
              max(m[:, lo + int((hi - lo) * i / 3):lo + int((hi - lo) * (i + 1) / 3)].sum(), 1)
              for i in range(3)]
    return (100.0 * sh.sum() / max(m.sum(), 1),
            100.0 * (sh & interior).sum() / max(interior.sum(), 1),
            thirds, int(np.where(sh.any(0))[0].mean()) if sh.any() else -1)


# --- 1. span_u vs an independent implementation on a synthetic blobby mask ------------

def span_u_ref(mask, axis):
    b = mask if axis == 1 else mask.T
    out = np.zeros_like(b, np.float32)
    for i in range(b.shape[0]):
        idx = np.flatnonzero(b[i])
        if idx.size == 0:
            continue
        splits = np.where(np.diff(idx) > 1)[0]
        for s, e in zip(np.r_[0, splits + 1], np.r_[splits, idx.size - 1]):
            run = idx[s:e + 1]
            out[i, run] = 1.0 - 2.0 * np.arange(run.size) / max(run.size - 1, 1)
    return out if axis == 1 else out.T


def check_span_u():
    rng = np.random.default_rng(7)
    mask = rng.random((64, 96)) > 0.55
    mask[5:40, 10:30] = True
    mask[45:60, 60:90] = True
    ok = True
    for axis in (0, 1):
        a, b = B.span_u(mask, axis), span_u_ref(mask, axis)
        diff = np.abs(a - b)[mask]
        ok &= bool(diff.max() < 1e-6)
        print(f"  span_u axis={axis}: max |v2 - independent ref| inside figure = {diff.max():.2e}")
    a = B.span_u(mask, 1)
    blob = np.arange(10, 30)
    mono = bool(np.all(np.diff(a[6, blob]) <= 1e-9))
    print(f"  blob row: u start={a[6, blob[0]]:+.2f} end={a[6, blob[-1]]:+.2f} monotonic={mono}")
    bg = B.form_map(np.dstack([np.zeros((64, 96, 3), np.uint8),
                               (mask * 255).astype(np.uint8)]))[0]
    bg_ok = bool(np.all(bg[~mask] == (128, 128, 255, 0)))
    print(f"  background forced to neutral normal / depth 0: {bg_ok}")
    return {"pass": bool(ok and mono and bg_ok)}


def check_nx_sign(cid):
    sp, n = load(cid)
    m = sp[:, :, 3] > 128
    rows = [r for r in range(0, m.shape[0], 37) if m[r].any()]
    lx = float(np.mean([n[r, np.flatnonzero(m[r])[0], 0] for r in rows]))
    rx = float(np.mean([n[r, np.flatnonzero(m[r])[-1], 0] for r in rows]))
    print(f"  {cid}: stored nx at silhouette  left={lx:+.3f} (want <0)  right={rx:+.3f} (want >0)")
    return {"left": round(lx, 3), "right": round(rx, 3), "pass": bool(lx < 0 < rx)}


# --- 2. score the candidate key geometries -------------------------------------------

KEYS = {
    "A shipped after billboard fix (-.88,.40,.30)": (-0.88, 0.40, 0.30),
    "B pre-fix, quad at identity   (-.88,.40,-.30)": (-0.88, 0.40, -0.30),
    "C quad flipped, key not flipped (+.88,.40,.30)": (0.88, 0.40, 0.30),
}
SCENE = B.SCENE_LIGHTS["frontal"]


def main():
    print("== span_u ==")
    report = {"span_u": check_span_u()}
    print("== stored nx sign ==")
    ids = sorted(d for d in os.listdir(OUT) if d.endswith("_da") and os.path.isdir(os.path.join(OUT, d)))
    report["nx_sign"] = check_nx_sign(ids[0])

    print(f"== key geometry over {len(ids)} DK sets (_KeyBias 0.15, frontal scene) ==")
    res = {k: [] for k in KEYS}
    sprite = None
    for cid in ids:
        try:
            sprite, n = load(cid)
        except FileNotFoundError:
            continue
        for name, key in KEYS.items():
            shipped = B._n((1 - B.SCENE_BIAS) * B._n(key) + B.SCENE_BIAS * B._n(SCENE))
            res[name].append(shade(sprite, n, shipped))
    print("%-46s %8s %9s  %s" % ("key geometry", "shadow%", "interior%", "L/M/R thirds · centroid col"))
    for name, rows in res.items():
        s = np.array([r[0] for r in rows]); i = np.array([r[1] for r in rows])
        t = np.array([r[2] for r in rows]); c = np.array([r[3] for r in rows])
        print("%-46s %7.1f%% %8.1f%%  %.0f/%.0f/%.0f · col %d (mid %d)" % (
            name, s.mean(), i.mean(), t[:, 0].mean(), t[:, 1].mean(), t[:, 2].mean(),
            c.mean(), sprite.shape[1] // 2))
        report.setdefault("keys", {})[name] = {
            "shadow_pct": [round(float(x), 1) for x in s],
            "interior_pct": [round(float(x), 1) for x in i],
            "thirds": [[round(float(x), 1) for x in r] for r in t],
            "centroid_col": [int(x) for x in c]}

    out = os.path.join(DOCS, "qc_keyframe.json")
    json.dump(report, open(out, "w"), indent=1)
    print("\nreport:", out)


if __name__ == "__main__":
    main()

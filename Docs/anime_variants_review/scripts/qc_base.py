#!/usr/bin/env python3
"""
QC: what the billboard fix changes for the ORIGINAL 16 base sets.

`AnimeCharacterActor` rotates the sprite quad 180° about Y so the camera sees the front of
Unity's built-in Quad (which faces -Z). That flips both x and z of every WORLD light when it
is expressed in the authored frame (+x = tangent/image right, +y = up, +z = mesh normal), so

    pre-fix key = (-x, y, -z) of the post-fix key.

Base sets ship `_NormalStrength 0.8` and `_KeyBias 1`, i.e. pure scene key, so they only ever
see the scene light — which means the fix is the whole story for them. Run before the fix they
were uniformly in the shadow band under any front/side/overhead light (lit backwards); after it
they render their painted colour under those lights.

Run:  python3 qc_base.py           (every set that has a form map and no _da suffix)
Writes normals/qc_base.json.
"""
import sys, os, json
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_formmap_v2 as B

OUT, DOCS = B.OUT, B.DOCS
STRENGTH = 0.8        # AnimeCel.mat default, unchanged for base sets


def shade(sprite, n, key, strength):
    m = sprite[:, :, 3] > 128
    nt = np.stack([n[..., 0] * strength, n[..., 1] * strength, np.ones(m.shape, np.float32)], -1)
    nt /= np.linalg.norm(nt, axis=-1, keepdims=True)
    l = np.array(key, np.float32); l /= np.linalg.norm(l)
    ndl = (nt[..., 0] * l[0] + nt[..., 1] * l[1] + nt[..., 2] * l[2]) * 0.5 + 0.5
    return 100.0 * (m & (B.smoothstep(0.43, 0.47, ndl) < 0.5)).sum() / max(m.sum(), 1)


def main():
    ids = sorted(d for d in os.listdir(OUT) if os.path.isdir(os.path.join(OUT, d))
                 and not d.endswith("_da")
                 and os.path.exists(os.path.join(OUT, d, f"{d}_normal.png")))
    print(f"base sets with a form map: {len(ids)} -> {ids}\n")
    print(f"{'set':16} " + "  ".join(f"{k}: pre->post" for k in B.SCENE_LIGHTS))
    rows = {}
    for cid in ids:
        d = os.path.join(OUT, cid)
        sprite = np.array(Image.open(os.path.join(d, f"{cid}_front.png")).convert("RGBA"))
        h, w = sprite.shape[:2]
        n = np.array(Image.open(os.path.join(d, f"{cid}_normal.png"))
                     .resize((w, h), Image.BILINEAR)).astype(np.float32) / 255.0 * 2 - 1
        cells = []
        for name, key in B.SCENE_LIGHTS.items():
            pre = shade(sprite, n, (-key[0], key[1], -key[2]), STRENGTH)   # quad at identity
            post = shade(sprite, n, key, STRENGTH)                          # quad turned 180°
            cells.append(f"{pre:.0f}->{post:.0f}")
            rows.setdefault(name, []).append((pre, post))
        print(f"{cid:16} " + "  ".join(cells))

    print("\nmean over base sets (shadow % of the figure):")
    summary = {}
    for name, v in rows.items():
        a = np.array(v)
        summary[name] = {"before": round(float(a[:, 0].mean()), 1),
                         "after": round(float(a[:, 1].mean()), 1),
                         "per_set_before": [round(float(x), 1) for x in a[:, 0]],
                         "per_set_after": [round(float(x), 1) for x in a[:, 1]]}
        print(f"  {name:9} before fix {a[:, 0].mean():5.1f}%   after fix {a[:, 1].mean():5.1f}%")
    out = os.path.join(DOCS, "qc_base.json")
    json.dump(summary, open(out, "w"), indent=1)
    print("\nreport:", out)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Test hair-colour separability for the 5 heroes (top-of-head region after rembg)."""
import os, glob, sys
import numpy as np
from PIL import Image
sys.path.insert(0, "/tmp/opencode")
from rembg import new_session, remove

DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
REF = "/tmp/opencode/anime_refs"
HERO = ["yami_yugi", "joey", "tea", "tristan", "bakura"]
sess = new_session("u2netp")


def hair(im):
    a = np.array(remove(im.convert("RGBA"), session=sess))
    m = a[:, :, 3] > 128
    if m.sum() < 500:
        return None
    ys, xs = np.where(m)
    y0, y1 = ys.min(), ys.max()
    H = y1 - y0 + 1
    top = a[y0:y0 + max(1, int(H * 0.22))]          # top ~22% = hair
    sel = top[top[:, :, 3] > 128]
    px = sel[:, :3].astype(float)
    # drop near-white background pixels
    px = px[(px.max(1) < 240) | (px.min(1) < 210)]
    if len(px) < 80:
        return None
    med = np.median(px, axis=0)
    return med, len(px)


for h in HERO:
    cols = []
    for p in ([os.path.join(DL, h, h + "_front.png"), os.path.join(WIKI, h)] +
              sorted(glob.glob(os.path.join(REF, h, "*")))):
        if not os.path.exists(p):
            continue
        try:
            r = hair(Image.open(p))
        except Exception:
            r = None
        if r:
            cols.append(r[0])
    if not cols:
        print(h, "none"); continue
    A = np.array(cols)
    print(f"{h:10} n={len(A):2}  median RGB={np.median(A,0).round(0)}  "
          f"spread(IQR)={np.round(np.percentile(A,75,0)-np.percentile(A,25,0),0)}  "
          f"ex R-G={(A[:,0]-A[:,1]).mean():6.1f} R-B={(A[:,0]-A[:,2]).mean():6.1f} lum={A.mean():.0f}")

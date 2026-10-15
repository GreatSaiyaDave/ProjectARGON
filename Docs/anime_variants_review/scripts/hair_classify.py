#!/usr/bin/env python3
"""
Hybrid hero classifier: CLIP similarity + hair-colour profile.
Prototypes (hair colour) come from each hero's DL art + anime portrait (clean, solo);
CLIP gallery as before. Validate on the independent anime refs (LOO), tune the mix.
"""
import os, glob, sys
import numpy as np
from PIL import Image
sys.path.insert(0, "/tmp/opencode")
import emblib as E
from rembg import new_session, remove

DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
REF = "/tmp/opencode/anime_refs"
HERO = ["yami_yugi", "joey", "tea", "tristan", "bakura"]
sess = new_session("u2netp")
cache = {}


def hair_feat(im):
    """[R,G,B (0-1), R-G, R-B, lum] of the top-of-head band after rembg; None if unusable."""
    key = getattr(im, "filename", None)
    if key and key in cache:
        return cache[key]
    a = np.array(remove(im.convert("RGBA"), session=sess))
    m = a[:, :, 3] > 128
    v = None
    if m.sum() >= 500:
        ys, xs = np.where(m)
        y0, y1 = ys.min(), ys.max(); H = y1 - y0 + 1
        top = a[y0:y0 + max(1, int(H * 0.22))]
        sel = top[top[:, :, 3] > 128][:, :3].astype(float)
        sel = sel[(sel.max(1) < 240) | (sel.min(1) < 210)]
        if len(sel) >= 80:
            r, g, b = np.median(sel, axis=0) / 255.0
            v = np.array([r, g, b, r - g, r - b, (r + g + b) / 3])
    if key:
        cache[key] = v
    return v


# hair prototypes from clean DL art (+ portrait)
proto = {}
for h in HERO:
    fs = [hair_feat(Image.open(os.path.join(DL, h, h + "_front.png"))),
          hair_feat(Image.open(os.path.join(WIKI, h)))]
    fs = [f for f in fs if f is not None]
    proto[h] = np.median(np.array(fs), axis=0)
    print(f"{h:10} prototype RGB={np.round(proto[h][:3]*255)}")


def hair_dist(f, h):
    # weight colour channels and the R-G / R-B / lum discriminators
    w = np.array([1.0, 1.0, 1.0, 1.3, 1.3, 1.1])
    return float(np.abs((f - proto[h]) * w).sum())


# CLIP gallery: (path, emb) so we can leave the query out
gal = {}
for h in HERO:
    v = [(None, E.embed(Image.open(os.path.join(DL, h, h + "_front.png")), "clip", flip=True)),
         (None, E.embed(Image.open(os.path.join(WIKI, h)), "clip", flip=True))]
    for p in sorted(glob.glob(os.path.join(REF, h, "*"))):
        try:
            im = Image.open(p)
            v.append((os.path.abspath(p), E.embed(im, "clip", flip=True)))
        except Exception:
            pass
    gal[h] = v

# queries: independent anime refs of all 16 chars
qs = []
for cid in E.CH:
    for p in sorted(glob.glob(os.path.join(REF, cid, "*"))):
        try:
            im = Image.open(p)
            qs.append((cid, os.path.abspath(p), E.embed(im, "clip", flip=True), hair_feat(im)))
        except Exception:
            pass


def run(wc, wh, label):
    heroq = [(c, p, e, f) for c, p, e, f in qs if c in HERO]
    nonq = [(c, p, e, f) for c, p, e, f in qs if c not in HERO]
    ok = 0
    for c, p, e, f in heroq:
        scores = []
        for h in HERO:
            s = wc * max(float(e @ g) for (gp, g) in gal[h] if gp != p)
            if f is not None:
                s -= wh * hair_dist(f, h)
            scores.append((s, h))
        scores.sort(reverse=True)
        ok += scores[0][1] == c
    fa = 0
    for c, p, e, f in nonq:
        scores = []
        for h in HERO:
            s = wc * max(float(e @ g) for (gp, g) in gal[h] if gp != p)
            if f is not None:
                s -= wh * hair_dist(f, h)
            scores.append((s, h))
        scores.sort(reverse=True)
        if scores[0][0] - scores[1][0] >= 0.03:
            fa += 1
    print(f"{label:26} hero top1 {ok}/{len(heroq)} ({ok/len(heroq):.0%})  non-hero accept {fa}/{len(nonq)}")


run(1.0, 0.0, "clip only")
run(1.0, 0.15, "clip + hair 0.15")
run(1.0, 0.30, "clip + hair 0.30")
run(1.0, 0.50, "clip + hair 0.50")
run(0.0, 1.0, "hair only")

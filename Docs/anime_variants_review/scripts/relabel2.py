#!/usr/bin/env python3
"""
Constrained character labelling using scraped per-episode cast metadata.

For each candidate we now only compare against the characters that the wiki says
appear in THAT episode (episode_cast.json). Restricted to a handful of people,
whole-figure colour matching becomes far more meaningful than comparing against
all 16 at once (which is what produced the earlier "everything is Mako" bias).

Outputs:
  * adds guess3 + m2 columns to every <slug>/candidates.tsv
  * by_episode/<slug>_<title>.jpg  -- header = episode title + cast, tiles = frame + guess
"""
import os, glob, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
DLCHARS = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
NAME = {"yami_yugi": "Yami Yugi", "kaiba": "Kaiba", "joey": "Joey", "mai": "Mai",
        "tea": "Tea", "weevil": "Weevil", "rex": "Rex", "mako": "Mako", "marik": "Marik",
        "bakura": "Bakura", "bandit_keith": "Bandit Keith", "ishizu": "Ishizu",
        "odion": "Odion", "pegasus": "Pegasus", "mokuba": "Mokuba", "tristan": "Tristan"}
BANDS = [(0.0, 0.20), (0.20, 0.55), (0.55, 1.0)]
BW = np.array([0.55, 0.25, 0.20])
cast = json.load(open(ROOT + "/episode_cast.json"))

try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 15)
    F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 13)
except Exception:
    F = F2 = ImageFont.load_default()


def _hist(px):
    if len(px) == 0:
        return np.zeros(64)
    q = (px // 64).astype(np.int32)
    h = np.bincount(q[:, 0] * 16 + q[:, 1] * 4 + q[:, 2], minlength=64).astype(float)
    return h / max(1.0, h.sum())


def bands(path):
    a = np.array(Image.open(path).convert("RGBA"))
    ys, xs = np.where(a[:, :, 3] > 128)
    if len(ys) == 0:
        return None
    c = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    H = c.shape[0]
    return [_hist(c[int(H * f0):max(int(H * f0) + 1, int(H * f1))][:, :, :3][
        c[int(H * f0):max(int(H * f0) + 1, int(H * f1))][:, :, 3] > 128]) for f0, f1 in BANDS]


for p in glob.glob(ROOT + "/by_episode/*.jpg"):
    os.remove(p)

refs = {}
for cid in NAME:
    p = os.path.join(DLCHARS, cid, cid + "_front.png")
    if os.path.exists(p):
        b = bands(p)
        if b:
            refs[cid] = b


def dist(a, b):
    return float(np.sum(BW * np.array([np.abs(x - y).sum() for x, y in zip(a, b)])))


for tsv in sorted(glob.glob(ROOT + "/dm_*/candidates.tsv")):
    slug = os.path.basename(os.path.dirname(tsv))
    pool = [c for c in cast.get(slug, []) if c in refs] or list(refs)
    lines = open(tsv).read().splitlines()
    hdr = lines[0].split("\t")
    if "guess3" not in hdr:
        hdr += ["guess3", "m2"]
    keep12 = [h for h in hdr if h not in ("guess3", "m2")]
    out = ["\t".join(hdr)]
    for line in lines[1:]:
        p = line.split("\t")[:len(keep12)]
        png = os.path.join(ROOT, slug, p[0] + ".png")
        b = bands(png) if os.path.exists(png) else None
        if b is None:
            g, m = "?", "0.00"
        else:
            ds = sorted((dist(b, refs[c]), c) for c in pool)
            (d1, c1) = ds[0]
            d2 = ds[1][0] if len(ds) > 1 else d1 * 2
            m = (d2 - d1) / (d2 + 1e-9)
            g = c1 if (m >= 0.12 and d1 <= 1.0) else "?"
            m = f"{m:.2f}"
        out.append("\t".join(p + [g, m]))
    open(tsv, "w").write("\n".join(out) + "\n")

    # per-episode sheet with cast header
    items = []
    for line in out[1:]:
        p = line.split("\t")
        d = dict(zip(hdr, p))
        kf = os.path.join(ROOT, slug, "kf", f"k_{int(d['frame'])+1:05d}.jpg")
        items.append(dict(d, kf=kf, png=os.path.join(ROOT, slug, d["id"] + ".png")))
    if not items:
        continue
    items.sort(key=lambda d: int(d["frame"]))
    cols, tw, th = 4, 430, 350
    rws = (len(items) + cols - 1) // cols
    sh = Image.new("RGB", (cols * tw, 40 + rws * th), (22, 22, 28))
    g = ImageDraw.Draw(sh)
    casttxt = ", ".join(NAME.get(c, c) for c in cast.get(slug, []))
    g.text((10, 10), f"{slug}   Cast: {casttxt}", fill=(255, 240, 140), font=F)
    for i, d in enumerate(items):
        x, y = (i % cols) * tw, 40 + (i // cols) * th
        fw = 0
        if os.path.exists(d["kf"]):
            fr = Image.open(d["kf"]).convert("RGB"); fr.thumbnail((tw - 150, th - 30))
            sh.paste(fr, (x + 4, y + 30)); fw = fr.size[0]
        cut = Image.open(d["png"]).convert("RGBA"); cut.thumbnail((130, th - 30))
        bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
        sh.paste(bg.convert("RGB"), (x + 12 + fw, y + 30))
        g.text((x + 4, y + 4), f"f{d['frame']}", fill=(255, 255, 120), font=F2)
        lab = NAME.get(d["guess3"], "?")
        g.text((x + 4, y + 18), f"~{lab}  (m{d['m2']})", fill=(150, 220, 255), font=F2)
    path = os.path.join(ROOT, "by_episode", slug + ".jpg")
    sh.save(path, quality=84)
    print("wrote", path, len(items), "| cast", len(cast.get(slug, [])))

print("done")

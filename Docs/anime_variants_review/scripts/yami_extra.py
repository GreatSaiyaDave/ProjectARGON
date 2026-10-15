#!/usr/bin/env python3
"""Expand the Yami Bakura full-frame set by scanning EVERY keyframe of e013/e014
("Evil Spirit of the Ring" / follow-up), keeping frames with a prominent figure and
ranking them by CLIP similarity to Bakura. Writes fullframes/yami_bakura/."""
import os, glob, sys, shutil, csv
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, "/tmp/opencode")
import emblib as E
from rembg import new_session, remove

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
REF = "/tmp/opencode/anime_refs"
EPS = ["dm_s1e013", "dm_s1e014"]
OUT = ROOT + "/fullframes/yami_bakura"
TOPN = 80
sess = new_session("u2netp")


def solo_ok(im):
    a = np.array(remove(im.convert("RGBA"), session=sess))[:, :, 3]
    m = a > 128
    if m.sum() < 800:
        return False
    ys, xs = np.where(m)
    h = ys.max() - ys.min() + 1; w = xs.max() - xs.min() + 1
    fill = m.sum() / (w * h)
    return not (w / h > 1.15 or h / a.shape[0] < 0.55 or fill < 0.22 or fill > 0.95)


gal = [E.embed(Image.open(os.path.join(DL, "bakura", "bakura_front.png")), "clip", flip=True),
       E.embed(Image.open(os.path.join(WIKI, "bakura")), "clip", flip=True)]
for p in sorted(glob.glob(os.path.join(REF, "bakura", "*"))):
    try:
        im = Image.open(p)
        if solo_ok(im):
            gal.append(E.embed(im, "clip", flip=True))
    except Exception:
        pass
print("bakura gallery:", len(gal))

rows = []
for slug in EPS:
    kfs = sorted(glob.glob(os.path.join(ROOT, slug, "kf", "*.jpg")))
    print(f"{slug}: {len(kfs)} keyframes")
    for kf in kfs:
        im = Image.open(kf).convert("RGB")
        W, H = im.size
        cut = remove(im.convert("RGBA"), session=sess)
        a = np.array(cut)[:, :, 3]; mask = a > 128
        if mask.sum() < 0.02 * W * H:
            continue
        ys, xs = np.where(mask)
        h = ys.max() - ys.min() + 1; w = xs.max() - xs.min() + 1
        if h < 0.42 * H or w / h > 1.45:
            continue
        fill = mask.sum() / (w * h)
        if not (0.12 < fill < 0.96):
            continue
        e = E.embed(cut, "clip", flip=True)
        s = max(float(e @ g) for g in gal)
        fr = int(os.path.basename(kf)[2:7]) - 1
        rows.append((s, slug, fr, kf))
print("figure frames:", len(rows))
rows.sort(reverse=True)
top = rows[:TOPN]

if os.path.isdir(OUT):
    shutil.rmtree(OUT)
os.makedirs(OUT)
try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 22)
    F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 15)
except Exception:
    F = F2 = ImageFont.load_default()
with open(os.path.join(OUT, "index.csv"), "w", newline="") as fh:
    w = csv.writer(fh); w.writerow(["file", "episode", "frame", "similarity"])
    for s, slug, fr, kf in top:
        fn = f"{slug}_f{fr}.jpg"
        shutil.copyfile(kf, os.path.join(OUT, fn))
        w.writerow([fn, slug[3:], fr, f"{s:.3f}"])

cols, tw, th = 4, 460, 290
rws = (len(top) + cols - 1) // cols
sh = Image.new("RGB", (cols * tw, 44 + rws * th), (18, 18, 22))
g_ = ImageDraw.Draw(sh)
g_.text((12, 10), f"Yami Bakura  — top {len(top)} full frames from e013/e014 (Evil Spirit of the Ring)",
        fill=(255, 240, 140), font=F)
for i, (s, slug, fr, kf) in enumerate(top):
    x, y = (i % cols) * tw, 44 + (i // cols) * th
    fi = Image.open(kf).convert("RGB"); fi.thumbnail((tw - 12, th - 34))
    sh.paste(fi, (x + 6, y + 4))
    g_.text((x + 6, y + th - 26), f"{slug[3:]}:f{fr}   sim {s:.2f}", fill=(180, 230, 255), font=F2)
sh.save(ROOT + "/fullframes/yami_bakura.jpg", quality=86)
print("wrote", len(top), "Yami Bakura frames")

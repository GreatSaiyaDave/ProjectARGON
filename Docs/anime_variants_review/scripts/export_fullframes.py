#!/usr/bin/env python3
"""
Export full-frame (1280x720) reference sets for game development.

For each target character, rank every candidate by CLIP similarity to that character and
copy the top-N *full frames* into fullframes/<cid>/ as s1eNNN_fFRAME.jpg, plus an index.csv
and a contact sheet fullframes/<cid>.jpg.

Yami Bakura is separated from Ryo Bakura by episode (the ring spirit's Shadow Duel is
e013 "Evil Spirit of the Ring" / e014); everything else Bakura is treated as Ryo.
"""
import os, glob, json, sys, shutil, csv
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, "/tmp/opencode")
import emblib as E
from rembg import new_session, remove

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
REF = "/tmp/opencode/anime_refs"

CHARS = ["yami_yugi", "joey", "tea", "tristan", "bakura", "yami_bakura"]
GAL = {"yami_yugi": "yami_yugi", "joey": "joey", "tea": "tea", "tristan": "tristan",
       "bakura": "bakura", "yami_bakura": "bakura"}
NAME = {"yami_yugi": "Yami Yugi", "joey": "Joey Wheeler", "tea": "Tea Gardner",
        "tristan": "Tristan Taylor", "bakura": "Ryo Bakura", "yami_bakura": "Yami Bakura"}
YAMI_EPS = {"dm_s1e013", "dm_s1e014"}
TOPN = 60
sess = new_session("u2netp")


def solo_ok(im):
    try:
        cut = remove(im.convert("RGBA"), session=sess)
    except Exception:
        return False
    a = np.array(cut)[:, :, 3]; m = a > 128
    if m.sum() < 800:
        return False
    ys, xs = np.where(m)
    h = ys.max() - ys.min() + 1; w = xs.max() - xs.min() + 1
    fill = m.sum() / (w * h)
    return not (w / h > 1.15 or h / a.shape[0] < 0.55 or fill < 0.22 or fill > 0.95)


gallery = {}
for g in set(GAL.values()):
    v = [E.embed(Image.open(os.path.join(DL, g, g + "_front.png")), "clip", flip=True),
         E.embed(Image.open(os.path.join(WIKI, g)), "clip", flip=True)]
    for p in sorted(glob.glob(os.path.join(REF, g, "*"))):
        try:
            im = Image.open(p)
            if solo_ok(im):
                v.append(E.embed(im, "clip", flip=True))
        except Exception:
            pass
    gallery[g] = v
print("galleries:", {g: len(v) for g, v in gallery.items()})

cands = []
for tsv in sorted(glob.glob(ROOT + "/dm_*/candidates.tsv")):
    slug = os.path.basename(os.path.dirname(tsv))
    for line in open(tsv).read().splitlines()[1:]:
        p = line.split("\t")
        png = os.path.join(ROOT, slug, p[0] + ".png")
        kf = os.path.join(ROOT, slug, "kf", f"k_{int(p[3])+1:05d}.jpg")
        if os.path.exists(png) and os.path.exists(kf):
            cands.append((slug, int(p[3]), png, kf))
print("candidates with frames:", len(cands))

outroot = ROOT + "/fullframes"
if os.path.isdir(outroot):
    shutil.rmtree(outroot)
os.makedirs(outroot)
try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 22)
    F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 15)
except Exception:
    F = F2 = ImageFont.load_default()

summary = {}
for c in CHARS:
    g = GAL[c]
    pool = cands
    if c == "yami_bakura":
        pool = [x for x in cands if x[0] in YAMI_EPS]
    elif c == "bakura":
        pool = [x for x in cands if x[0] not in YAMI_EPS]
    scored = []
    for slug, fr, png, kf in pool:
        e = E.embed(Image.open(png), "clip", flip=True)
        scored.append((max(float(e @ gv) for gv in gallery[g]), slug, fr, kf))
    scored.sort(reverse=True)
    top = scored[:TOPN]
    d = os.path.join(outroot, c)
    os.makedirs(d)
    with open(os.path.join(d, "index.csv"), "w", newline="") as fh:
        w = csv.writer(fh); w.writerow(["file", "episode", "frame", "similarity"])
        for s, slug, fr, kf in top:
            fn = f"{slug}_f{fr}.jpg"
            shutil.copyfile(kf, os.path.join(d, fn))
            w.writerow([fn, slug[3:], fr, f"{s:.3f}"])
    # contact sheet: 4 cols of full frames
    cols, tw, th = 4, 460, 290
    rws = (len(top) + cols - 1) // cols
    sh = Image.new("RGB", (cols * tw, 44 + rws * th), (18, 18, 22))
    g_ = ImageDraw.Draw(sh)
    g_.text((12, 10), f"{NAME[c]}  — top {len(top)} full frames (whole 1280x720 source frames)",
            fill=(255, 240, 140), font=F)
    for i, (s, slug, fr, kf) in enumerate(top):
        x, y = (i % cols) * tw, 44 + (i // cols) * th
        fr_img = Image.open(kf).convert("RGB")
        fr_img.thumbnail((tw - 12, th - 34))
        sh.paste(fr_img, (x + 6, y + 4))
        g_.text((x + 6, y + th - 26), f"{slug[3:]}:f{fr}   sim {s:.2f}", fill=(180, 230, 255), font=F2)
    sh.save(os.path.join(outroot, c + ".jpg"), quality=86)
    summary[NAME[c]] = len(top)
    print(f"  {NAME[c]:14} {len(top):3} frames -> fullframes/{c}/")

json.dump(summary, open(outroot + "/_summary.json", "w"), indent=1)
print("done:", summary)

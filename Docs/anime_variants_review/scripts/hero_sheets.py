#!/usr/bin/env python3
"""
Per-hero ranked contact sheets. Instead of an argmax label, score every candidate by its
similarity to each hero (max over that hero's gallery: DL art + anime portrait + solo anime
refs) and show the top matches per hero. The user scans and picks; no forced argmax sink.
Also writes heroes/top.tsv  (slug, frame, best hero, sim) for reference.
"""
import os, glob, json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, "/tmp/opencode")
import emblib as E
from rembg import new_session, remove

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
REF = "/tmp/opencode/anime_refs"
HERO = ["yami_yugi", "joey", "tea", "tristan", "bakura"]
NAME = {"yami_yugi": "Yami Yugi", "joey": "Joey", "tea": "Tea", "tristan": "Tristan",
        "bakura": "Yami Bakura"}
TOP = 72
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


gal = {}
for h in HERO:
    v = [E.embed(Image.open(os.path.join(DL, h, h + "_front.png")), "clip", flip=True),
         E.embed(Image.open(os.path.join(WIKI, h)), "clip", flip=True)]
    for p in sorted(glob.glob(os.path.join(REF, h, "*"))):
        try:
            im = Image.open(p)
            if solo_ok(im):
                v.append(E.embed(im, "clip", flip=True))
        except Exception:
            pass
    gal[h] = v
print("gallery:", {NAME[h]: len(v) for h, v in gal.items()})

rows = []
for tsv in sorted(glob.glob(ROOT + "/dm_*/candidates.tsv")):
    slug = os.path.basename(os.path.dirname(tsv))
    for line in open(tsv).read().splitlines()[1:]:
        p = line.split("\t")
        png = os.path.join(ROOT, slug, p[0] + ".png")
        if not os.path.exists(png):
            continue
        e = E.embed(Image.open(png), "clip", flip=True)
        sc = {h: max(float(e @ g) for g in gal[h]) for h in HERO}
        bh = max(sc, key=sc.get)
        rows.append((slug, int(p[3]), png, sc, bh))

os.makedirs(ROOT + "/heroes", exist_ok=True)
for p in glob.glob(ROOT + "/heroes/*.jpg"):
    os.remove(p)
try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 16)
    F3 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 12)
except Exception:
    F = F3 = ImageFont.load_default()

lines = ["slug\tframe\tbest\tsim"]
for h in HERO:
    its = sorted(rows, key=lambda r: -r[3][h])[:TOP]
    cols, tw, th = 8, 190, 268
    rws = (len(its) + cols - 1) // cols
    sh = Image.new("RGB", (cols * tw, 30 + rws * th), (22, 22, 28))
    g_ = ImageDraw.Draw(sh)
    g_.text((8, 7), f"{NAME[h]}  — top {len(its)} candidates by similarity  (best first)",
            fill=(255, 240, 140), font=F)
    for i, (slug, fr, png, sc, bh) in enumerate(its):
        x, y = (i % cols) * tw, 30 + (i // cols) * th
        cut = Image.open(png).convert("RGBA"); cut.thumbnail((tw - 8, th - 34))
        bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
        sh.paste(bg.convert("RGB"), (x + 4, y + 4))
        g_.text((x + 4, y + th - 28), f"{slug[3:]}:f{fr}", fill=(255, 255, 120), font=F3)
        col = (150, 255, 170) if bh == h else (170, 170, 180)
        g_.text((x + 4, y + th - 15), f"s{sc[h]:.2f} {NAME[bh].split()[0][:4]}", fill=col, font=F3)
        lines.append(f"{slug}\t{fr}\t{NAME[bh]}\t{sc[h]:.3f}")
    sh.save(os.path.join(ROOT, "heroes", h + ".jpg"), quality=85)

open(ROOT + "/heroes/top.tsv", "w").write("\n".join(lines) + "\n")
print("wrote heroes/ sheets:", [h + ".jpg" for h in HERO])
for h in HERO:
    its = sorted(rows, key=lambda r: -r[3][h])[:10]
    print(f"  {NAME[h]:12} " + "  ".join(f"{s[3:]}:f{f}" for s, f, _, _, _ in its))

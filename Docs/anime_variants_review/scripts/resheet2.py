#!/usr/bin/env python3
"""
Review sheets built on scraped metadata (no character guesses on tiles).

by_episode/<slug>.jpg : header = "s1eNNN  Title  --  Cast: ..." (from Yugipedia),
                        tiles = original keyframe + cutout, labelled f<frame>.
CHARACTER_INDEX.md    : per character, the episodes to look in (from the cast data),
                        with tile counts. Rare characters = the episodes worth mining.
"""
import os, glob, json
from PIL import Image, ImageDraw, ImageFont

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
meta = json.load(open(ROOT + "/episode_meta.json"))
NAME = {"yami_yugi": "Yami Yugi", "kaiba": "Kaiba", "joey": "Joey", "mai": "Mai",
        "tea": "Tea", "weevil": "Weevil", "rex": "Rex", "mako": "Mako", "marik": "Marik",
        "bakura": "Bakura", "bandit_keith": "Bandit Keith", "ishizu": "Ishizu",
        "odion": "Odion", "pegasus": "Pegasus", "mokuba": "Mokuba", "tristan": "Tristan"}

for p in glob.glob(ROOT + "/by_episode/*.jpg"):
    os.remove(p)
try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 16)
    F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 14)
except Exception:
    F = F2 = ImageFont.load_default()


def load(slug):
    out = []
    tsv = os.path.join(ROOT, slug, "candidates.tsv")
    if not os.path.exists(tsv):
        return out
    for line in open(tsv).read().splitlines()[1:]:
        p = line.split("\t")
        if len(p) < 4:
            continue
        png = os.path.join(ROOT, slug, p[0] + ".png")
        kf = os.path.join(ROOT, slug, "kf", f"k_{int(p[3])+1:05d}.jpg")
        if os.path.exists(png):
            out.append(dict(frame=int(p[3]), kf=kf, png=png))
    out.sort(key=lambda d: d["frame"])
    return out


COL, TW, TH, HD = 4, 430, 350, 36
index = {}
for slug in sorted(meta):
    items = load(slug)
    if not items:
        continue
    title = meta[slug]["title"]
    strong = meta[slug]["cast_strong"]
    used = strong if strong else meta[slug]["cast"]
    for c in used:
        index.setdefault(c, []).append((slug, len(items)))
    rws = (len(items) + COL - 1) // COL
    sh = Image.new("RGB", (COL * TW, HD + rws * TH), (22, 22, 28))
    g = ImageDraw.Draw(sh)
    casttxt = ", ".join(NAME.get(c, c) for c in used) or "-"
    g.text((10, 9), f"{slug}   {title}   |   Cast: {casttxt}", fill=(255, 240, 140), font=F)
    for i, d in enumerate(items):
        x, y = (i % COL) * TW, HD + (i // COL) * TH
        fw = 0
        if os.path.exists(d["kf"]):
            fr = Image.open(d["kf"]).convert("RGB"); fr.thumbnail((TW - 150, TH - 26))
            sh.paste(fr, (x + 4, y + 26)); fw = fr.size[0]
        cut = Image.open(d["png"]).convert("RGBA"); cut.thumbnail((132, TH - 26))
        bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
        sh.paste(bg.convert("RGB"), (x + 12 + fw, y + 26))
        g.text((x + 4, y + 4), f"{slug[3:]}:f{d['frame']}", fill=(255, 255, 120), font=F2)
    sh.save(os.path.join(ROOT, "by_episode", slug + ".jpg"), quality=84)

lines = ["# Character -> episodes (from Yugipedia cast), with tile counts\n",
         "Rare characters are easy: few episodes to scan.\n"]
for c in sorted(index, key=lambda c: len(index[c])):
    eps = index[c]
    lines.append(f"\n## {NAME.get(c,c)}  ({len(eps)} episodes, {sum(n for _,n in eps)} tiles)")
    lines.append("  " + ", ".join(f"{s[3:]}({n})" for s, n in eps))
open(ROOT + "/CHARACTER_INDEX.md", "w").write("\n".join(lines) + "\n")
print("\n".join(lines))

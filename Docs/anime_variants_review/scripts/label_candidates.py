#!/usr/bin/env python3
"""
Final labeller (v2). CLIP image embedding, gallery = Duel Links art + anime portrait,
flip-TTA. Validated: 74% top-1 / 80% top-3 on independent anime images.

Ranking is restricted to the episode's cast (a top-1 outside the cast is almost always
an extra or a mis-ID). Confidence tiers on the cast-restricted margin:

  tier 2 (strong): margin >= 0.05 and best >= 0.75   -> green "Name"
  tier 1 (weak):   margin >= 0.03 and best >= 0.72   -> amber "Name?"
  tier 0:                                            -> grey "?"

Writes guess4/conf/margin into each candidates.tsv, then rebuilds:
  by_episode/<slug>.jpg  : cast header + tiles "fN Name (m0.08)"
  by_character/<cid>.jpg : every tier>=1 tile for that character, best-margin first
"""
import os, glob, json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, "/tmp/opencode")
import emblib as E

ROOT = "/home/greatsaiyadave/argon-avatar/research/anime_variants"
DL = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
WIKI = "/tmp/opencode/wiki_chars"
NAME = {"yami_yugi": "Yami Yugi", "kaiba": "Kaiba", "joey": "Joey", "mai": "Mai",
        "tea": "Tea", "weevil": "Weevil", "rex": "Rex", "mako": "Mako", "marik": "Marik",
        "bakura": "Bakura", "bandit_keith": "Bandit Keith", "ishizu": "Ishizu",
        "odion": "Odion", "pegasus": "Pegasus", "mokuba": "Mokuba", "tristan": "Tristan"}
meta = json.load(open(ROOT + "/episode_meta.json"))

D = {c: E.embed(Image.open(os.path.join(DL, c, c + "_front.png")), "clip", flip=True)
     for c in E.CH if os.path.exists(os.path.join(DL, c, c + "_front.png"))}
W = {c: E.embed(Image.open(os.path.join(WIKI, c)), "clip", flip=True)
     for c in E.CH if os.path.exists(os.path.join(WIKI, c))}

try:
    F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 16)
    F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 14)
    F3 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 12)
except Exception:
    F = F2 = F3 = ImageFont.load_default()


def classify(png, cast):
    e = E.embed(Image.open(png), "clip", flip=True)
    sims = sorted((max([float(e @ m[c]) for m in (D, W) if c in m]), c)
                  for c in E.CH if any(c in m for m in (D, W)))
    sims.reverse()
    pool = [s for s in sims if s[1] in cast] if cast else sims
    if not pool:
        pool = sims
    best, c1 = pool[0]
    marg = best - (pool[1][0] if len(pool) > 1 else 0)
    if marg >= 0.05 and best >= 0.75:
        tier = 2
    elif marg >= 0.03 and best >= 0.72:
        tier = 1
    else:
        tier = 0
    return c1, tier, best, marg


for p in glob.glob(ROOT + "/by_character/*.jpg") + glob.glob(ROOT + "/by_episode/*.jpg"):
    os.remove(p)
os.makedirs(ROOT + "/by_character", exist_ok=True)

bychar = {}
tiers = [0, 0, 0]
for tsv in sorted(glob.glob(ROOT + "/dm_*/candidates.tsv")):
    slug = os.path.basename(os.path.dirname(tsv))
    cast = meta.get(slug, {}).get("cast_strong") or meta.get(slug, {}).get("cast") or []
    lines = open(tsv).read().splitlines()
    hdr = lines[0].split("\t")
    base = [h for h in hdr if h not in ("guess4", "conf", "margin")]
    hdr = base + ["guess4", "conf", "margin"]
    out = ["\t".join(hdr)]
    items = []
    for line in lines[1:]:
        p = line.split("\t")[:len(base)]
        png = os.path.join(ROOT, slug, p[0] + ".png")
        if not os.path.exists(png):
            continue
        g, tier, b, m = classify(png, cast)
        tiers[tier] += 1
        out.append("\t".join(p + [g if tier else "?", f"{b:.3f}", f"{m:.3f}"]))
        d = dict(zip(hdr, out[-1].split("\t")))
        d["slug"] = slug
        d["kf"] = os.path.join(ROOT, slug, "kf", f"k_{int(d['frame'])+1:05d}.jpg")
        d["png"] = png
        d["tier"] = tier
        items.append(d)
        if tier >= 1:
            bychar.setdefault(g, []).append(d)
    open(tsv, "w").write("\n".join(out) + "\n")

    items.sort(key=lambda d: int(d["frame"]))
    title = meta.get(slug, {}).get("title", "")
    casttxt = ", ".join(NAME.get(c, c) for c in cast) or "-"
    cols, tw, th, hd = 4, 440, 350, 38
    rws = (len(items) + cols - 1) // cols
    sh = Image.new("RGB", (cols * tw, hd + rws * th), (22, 22, 28))
    g_ = ImageDraw.Draw(sh)
    g_.text((10, 10), f"{slug}  {title}   |   Cast: {casttxt}", fill=(255, 240, 140), font=F)
    for i, d in enumerate(items):
        x, y = (i % cols) * tw, hd + (i // cols) * th
        fw = 0
        if os.path.exists(d["kf"]):
            fr = Image.open(d["kf"]).convert("RGB")
            fr.thumbnail((tw - 155, th - 30))
            sh.paste(fr, (x + 4, y + 28)); fw = fr.size[0]
        cut = Image.open(d["png"]).convert("RGBA")
        cut.thumbnail((135, th - 30))
        bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
        sh.paste(bg.convert("RGB"), (x + 12 + fw, y + 28))
        g_.text((x + 4, y + 4), f"f{d['frame']}", fill=(255, 255, 120), font=F2)
        if d["tier"] == 2:
            lab, col = NAME.get(d["guess4"], d["guess4"]), (150, 255, 170)
        elif d["tier"] == 1:
            lab, col = NAME.get(d["guess4"], d["guess4"]) + "?", (255, 205, 120)
        else:
            lab, col = "?", (150, 150, 160)
        g_.text((x + 4, y + 18), f"~{lab} (m{d['margin']})", fill=col, font=F2)
    sh.save(os.path.join(ROOT, "by_episode", slug + ".jpg"), quality=84)

for cid, its in bychar.items():
    its.sort(key=lambda d: (-d["tier"], -float(d["margin"])))
    cols, tw, th = 8, 190, 270
    rws = (len(its) + cols - 1) // cols
    sh = Image.new("RGB", (cols * tw, 30 + rws * th), (22, 22, 28))
    g_ = ImageDraw.Draw(sh)
    n2 = sum(1 for d in its if d["tier"] == 2)
    g_.text((8, 7), f"{NAME.get(cid,cid)}  ({len(its)} tiles, {n2} strong, best first)",
            fill=(255, 240, 140), font=F)
    for i, d in enumerate(its[:400]):
        x, y = (i % cols) * tw, 30 + (i // cols) * th
        cut = Image.open(d["png"]).convert("RGBA")
        cut.thumbnail((tw - 8, th - 34))
        bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
        sh.paste(bg.convert("RGB"), (x + 4, y + 4))
        g_.text((x + 4, y + th - 28), f"{d['slug'][3:]}:f{d['frame']}", fill=(255, 255, 120), font=F3)
        c = (150, 255, 170) if d["tier"] == 2 else (255, 205, 120)
        g_.text((x + 4, y + th - 15), f"m{d['margin']}", fill=c, font=F3)
    sh.save(os.path.join(ROOT, "by_character", cid + ".jpg"), quality=84)

print("tiers:", {"strong": tiers[2], "weak": tiers[1], "none": tiers[0]},
      f"-> {tiers[1]+tiers[2]}/{sum(tiers)} labelled")
print("per character:", {NAME.get(c, c): len(v) for c, v in sorted(bychar.items(), key=lambda x: -len(x[1]))})

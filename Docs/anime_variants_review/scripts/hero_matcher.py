#!/usr/bin/env python3
"""
Hero-focused matcher. Label space restricted to the 5 heroes (Yami Yugi, Joey, Tea,
Tristan, Yami Bakura) using a rich per-hero gallery: Duel Links art + anime portrait +
quality-filtered anime reference images. A small label space plus many references should
beat the 16-way matcher for these characters.

Modes:
  --validate   LOO over the independent anime refs (all 16 chars, so we also measure the
               false-accept rate of non-heroes).
  (default)    label every candidate, write guess5/conf5/margin5, rebuild hero sheets.
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
meta = json.load(open(ROOT + "/episode_meta.json"))
sess = new_session("u2netp")


def solo_ok(im):
    try:
        cut = remove(im.convert("RGBA"), session=sess)
    except Exception:
        return False
    a = np.array(cut)[:, :, 3]
    m = a > 128
    if m.sum() < 800:
        return False
    ys, xs = np.where(m)
    h = ys.max() - ys.min() + 1
    w = xs.max() - xs.min() + 1
    fill = m.sum() / (w * h)
    return not (w / h > 1.15 or h / a.shape[0] < 0.55 or fill < 0.22 or fill > 0.95)


def build_gallery():
    gal = {}
    for h in HERO:
        refs = []
        refs.append((None, E.embed(Image.open(os.path.join(DL, h, h + "_front.png")), "clip", flip=True)))
        refs.append((None, E.embed(Image.open(os.path.join(WIKI, h)), "clip", flip=True)))
        for p in sorted(glob.glob(os.path.join(REF, h, "*"))):
            try:
                im = Image.open(p)
                if solo_ok(im):
                    refs.append((os.path.abspath(p), E.embed(im, "clip", flip=True)))
            except Exception:
                pass
        gal[h] = refs
    return gal


def classify(e, gal, exclude=None):
    sims = []
    for h in HERO:
        ss = [float(e @ emb) for (p, emb) in gal[h] if p != exclude]
        if ss:
            sims.append((max(ss), h))
    sims.sort(reverse=True)
    return sims[0][1], sims[0][0], (sims[0][0] - sims[1][0] if len(sims) > 1 else 0)


def validate(gal):
    kept = []
    for cid in E.CH:
        for p in sorted(glob.glob(os.path.join(REF, cid, "*"))):
            try:
                im = Image.open(p)
                if solo_ok(im):
                    kept.append((cid, os.path.abspath(p), E.embed(im, "clip", flip=True)))
            except Exception:
                pass
    hero_q = [(c, p, e) for c, p, e in kept if c in HERO]
    nonh_q = [(c, p, e) for c, p, e in kept if c not in HERO]
    ok = ok3 = 0
    for c, p, e in hero_q:
        c1, b, m = classify(e, gal, exclude=p)
        ok += c1 == c
    print(f"hero-ref LOO (5-way): top1 {ok}/{len(hero_q)}  ({ok/len(hero_q):.0%})")
    for thr in (0.0, 0.02, 0.04, 0.06):
        fa = sum(1 for c, p, e in nonh_q if classify(e, gal)[2] >= thr)
        print(f"  non-hero refs accepted as a hero at margin>={thr}: {fa}/{len(nonh_q)}")
    return kept


# candidate labelling + sheets
def sheets(gal):
    try:
        F = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 16)
        F2 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 14)
        F3 = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 12)
    except Exception:
        F = F2 = F3 = ImageFont.load_default()
    outdir = ROOT + "/heroes"
    os.makedirs(outdir, exist_ok=True)
    for p in glob.glob(outdir + "/*.jpg"):
        os.remove(p)
    bychar = {}
    for tsv in sorted(glob.glob(ROOT + "/dm_*/candidates.tsv")):
        slug = os.path.basename(os.path.dirname(tsv))
        cast = meta.get(slug, {}).get("cast_strong") or meta.get(slug, {}).get("cast") or []
        rows = [l.split("\t") for l in open(tsv).read().splitlines()]
        hdr = rows[0]
        for extra in ("guess5", "conf5", "margin5"):
            if extra not in hdr:
                hdr.append(extra)
        base = [h for h in hdr if h not in ("guess5", "conf5", "margin5")]
        out = ["\t".join(hdr)]
        for r in rows[1:]:
            p = r[:len(base)]
            png = os.path.join(ROOT, slug, p[0] + ".png")
            if not os.path.exists(png):
                continue
            e = E.embed(Image.open(png), "clip", flip=True)
            c1, b, m = classify(e, gal)
            out.append("\t".join(p + [c1, f"{b:.3f}", f"{m:.3f}"]))
            d = dict(zip(hdr, out[-1].split("\t")))
            d["slug"] = slug; d["margin"] = m; d["png"] = png
            d["in_cast"] = c1 in cast
            bychar.setdefault(c1, []).append(d)
        open(tsv, "w").write("\n".join(out) + "\n")
    for h, its in bychar.items():
        its.sort(key=lambda d: -d["margin"])
        cols, tw, th = 8, 190, 270
        rws = (len(its) + cols - 1) // cols
        sh = Image.new("RGB", (cols * tw, 30 + rws * th), (22, 22, 28))
        g_ = ImageDraw.Draw(sh)
        insc = sum(1 for d in its if d["in_cast"])
        g_.text((8, 7), f"{NAME.get(h,h)}  ({len(its)} tiles, {insc} in-cast, best margin first)",
                fill=(255, 240, 140), font=F)
        for i, d in enumerate(its[:400]):
            x, y = (i % cols) * tw, 30 + (i // cols) * th
            cut = Image.open(d["png"]).convert("RGBA")
            cut.thumbnail((tw - 8, th - 34))
            bg = Image.new("RGBA", cut.size, (55, 55, 66, 255)); bg.alpha_composite(cut)
            sh.paste(bg.convert("RGB"), (x + 4, y + 4))
            g_.text((x + 4, y + th - 28), f"{d['slug'][3:]}:f{d['frame']}", fill=(255, 255, 120), font=F3)
            c = (150, 255, 170) if d["in_cast"] else (150, 150, 160)
            g_.text((x + 4, y + th - 15), f"m{d['margin']:.2f}", fill=c, font=F3)
        sh.save(os.path.join(outdir, h + ".jpg"), quality=85)
    print("hero tiles:", {NAME.get(h, h): len(v) for h, v in sorted(bychar.items(), key=lambda x: -len(x[1]))})


if __name__ == "__main__":
    gal = build_gallery()
    print("gallery sizes:", {NAME.get(h, h): len(v) for h, v in gal.items()})
    if "--validate" in sys.argv:
        validate(gal)
    else:
        sheets(gal)

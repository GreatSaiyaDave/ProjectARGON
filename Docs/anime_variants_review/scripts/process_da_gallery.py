#!/usr/bin/env python3
"""
Turn GravekeepersBrewer's era galleries (front-facing renders on pure black) into ARGON
2D anime sprite sets.

Pipeline per render:
  1. key the black background to alpha (flood-fill the near-black region connected to the
     image border; interior near-black stays opaque, so a dark costume is not punched out);
  2. erode the matte 1px to drop the black fringe where a dark edge met the background;
  3. crop to the subject, scale to <=92% of a 1024 canvas, centre horizontally, drop the feet
     8% up from the bottom (the project's sprite convention);
  4. write the 12 contract files (4 angles + 3 expressions + 5 gestures; all identical, the
     billboard always faces the camera) into
     Assets/Resources/WRLDZ/AnimeCharacters/<catalog_id>_da/.

Renders are front-facing only, which is what the contract needs.
"""
import os, sys, json
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

HERE = "/home/greatsaiyadave/argon-avatar/research"
GAL = os.path.join(HERE, "anime_variants", "da_gallery")
DEST = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
PREVIEW = os.path.join(HERE, "anime_variants", "da_gallery_preview")

# catalog id -> (era gallery slug, source slug)   (Duelist Kingdom = Season 1 outfits)
MAP = {
    "yami_yugi":     ("duelist_kingdom", "yami_yugi"),
    "yugi":          ("duelist_kingdom", "yugi_muto"),
    "kaiba":         ("duelist_kingdom", "seto_kaiba"),
    "joey":          ("duelist_kingdom", "joey_wheeler"),
    "mai":           ("duelist_kingdom", "mai_valentine"),
    "tea":           ("duelist_kingdom", "tea_gardner"),
    "tristan":       ("duelist_kingdom", "tristan_taylor"),
    "bakura":        ("duelist_kingdom", "bakura_ryou"),
    "yami_bakura":   ("duelist_kingdom", "yami_bakura"),
    "pegasus":       ("duelist_kingdom", "maximillion_pegasus"),
    "weevil":        ("duelist_kingdom", "weevil_underwood"),
    "rex":           ("duelist_kingdom", "rex_raptor"),
    "mako":          ("duelist_kingdom", "mako_tsunami"),
    "bandit_keith":  ("duelist_kingdom", "bandit_keith"),
    "mokuba":        ("duelist_kingdom", "mokuba_kaiba"),
}

THRESH = 30          # sum(RGB) <= THRESH counts as background black
CANVAS = 1024


def key_black(path):
    img = Image.open(path).convert("RGB")
    a = np.array(img).astype(np.int16)
    neardark = a.sum(axis=2) <= THRESH
    lab, num = ndimage.label(neardark)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])))
    border.discard(0)
    bg = np.isin(lab, list(border)) if border else np.zeros_like(neardark)
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    alpha = np.array(Image.fromarray(alpha).filter(ImageFilter.MinFilter(3)))  # 1px erode
    out = Image.fromarray(np.dstack([np.array(img), alpha]), "RGBA")
    return out


def place(img):
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    w, h = img.size
    c = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    target = int(CANVAS * 0.92)
    if max(w, h) > target:
        if w > h:
            nw, nh = target, max(1, int(h * target / w))
        else:
            nw, nh = max(1, int(w * target / h)), target
        img = img.resize((nw, nh), Image.LANCZOS)
    w, h = img.size
    c.paste(img, ((CANVAS - w) // 2, CANVAS - int(CANVAS * 0.08) - h), img)
    return c


def stems(cid):
    return [f"{cid}_da_front", f"{cid}_da_34", f"{cid}_da_side", f"{cid}_da_back",
            "expr_neutral", "expr_serious", "expr_smiling",
            "gesture_idle", "gesture_draw", "gesture_summon", "gesture_attack", "gesture_defeat"]


def main():
    os.makedirs(PREVIEW, exist_ok=True)
    review = []
    for cid, (era, slug) in MAP.items():
        src = os.path.join(GAL, era, slug + ".png")
        if not os.path.exists(src):
            print(f"SKIP {cid}: missing {src}")
            continue
        keyed = key_black(src)
        frac = float((np.array(keyed)[..., 3] > 16).mean())
        if not (0.05 < frac < 0.80):
            print(f"WARN {cid}: keyed subject fraction {frac:.2f} looks wrong; skipping")
            continue
        placed = place(keyed)
        outdir = os.path.join(DEST, cid + "_da")
        os.makedirs(outdir, exist_ok=True)
        for s in stems(cid):
            placed.save(os.path.join(outdir, s + ".png"), optimize=True)
        review.append((cid, placed))
        print(f"{cid:13} <- {era}/{slug}   keyed_frac={frac:.2f}  -> {cid}_da/")

    # review sheet: source keyed cutout (RGBA over checker) for each set built
    if review:
        cols = 5
        tw, th = 300, 380
        rows = (len(review) + cols - 1) // cols
        sheet = Image.new("RGB", (cols * tw, rows * th), (24, 24, 30))
        for i, (cid, im) in enumerate(review):
            t = im.copy(); t.thumbnail((tw - 16, th - 16))
            bg = Image.new("RGBA", (tw - 16, th - 16), (70, 70, 80, 255))
            for y in range(0, bg.height, 16):
                for x in range(0, bg.width, 16):
                    if (x // 16 + y // 16) % 2:
                        bg.paste((96, 96, 108, 255), (x, y, min(x + 16, bg.width), min(y + 16, bg.height)))
            bg.alpha_composite(t, ((bg.width - t.width) // 2, (bg.height - t.height) // 2))
            sheet.paste(bg.convert("RGB"), ((i % cols) * tw + 8, (i // cols) * th + 8))
        sheet.save(os.path.join(PREVIEW, "dk_da_sets.png"), optimize=True)
        print("preview:", os.path.join(PREVIEW, "dk_da_sets.png"))

    print("built", len(review), "sets")


if __name__ == "__main__":
    main()

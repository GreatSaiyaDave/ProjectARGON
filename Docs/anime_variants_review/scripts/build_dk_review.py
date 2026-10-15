#!/usr/bin/env python3
"""
Build the user-facing review sheets for the Duelist Kingdom (Season 1) renders.

  DK_render_all.png          grid of every *_da set built from the DK gallery
  DK_render_vs_official.png  per character: the previous look (official-art set, or the
                             modular avatar placeholder) beside the new DK render

Output: ProjectARGON/Docs/anime_variants_review/da_pick/
"""
import os
from PIL import Image, ImageDraw, ImageFont

ASSETS = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"
OUT = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Docs/anime_variants_review/da_pick"

# (catalog id, display name, previous base set id or None)
ROWS = [
    ("yugi",         "Yugi Muto",        None),
    ("yami_yugi",    "Yami Yugi",        "yami_yugi"),
    ("kaiba",        "Seto Kaiba",       "kaiba"),
    ("joey",         "Joey Wheeler",     "joey"),
    ("mai",          "Mai Valentine",    "mai"),
    ("tea",          "Téa Gardner",      "tea"),
    ("tristan",      "Tristan Taylor",   "tristan"),
    ("bakura",       "Ryo Bakura",       "bakura"),
    ("yami_bakura",  "Yami Bakura",      None),
    ("pegasus",      "Maximillion Pegasus", "pegasus"),
    ("weevil",       "Weevil Underwood", "weevil"),
    ("rex",          "Rex Raptor",       "rex"),
    ("mako",         "Mako Tsunami",     "mako"),
    ("bandit_keith", "Bandit Keith",     "bandit_keith"),
    ("mokuba",       "Mokuba Kaiba",     "mokuba"),
]


def font(sz):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def checker(w, h, a=70, b=96):
    im = Image.new("RGB", (w, h), (a, a, a + 8))
    d = ImageDraw.Draw(im)
    for y in range(0, h, 20):
        for x in range(0, w, 20):
            if (x // 20 + y // 20) % 2:
                d.rectangle([x, y, x + 19, y + 19], fill=(b, b, b + 12))
    return im


def load_front(setid, which="front"):
    p = os.path.join(ASSETS, setid, f"{setid}_{which}.png")
    if not os.path.exists(p):
        p2 = os.path.join(ASSETS, setid, f"{setid}.png")
        return Image.open(p2).convert("RGBA") if os.path.exists(p2) else None
    return Image.open(p).convert("RGBA")


def fit(img, box):
    if img is None:
        return None
    t = img.copy()
    t.thumbnail(box, Image.LANCZOS)
    return t


def main():
    os.makedirs(OUT, exist_ok=True)
    F_TITLE = font(30)
    F_NAME = font(22)
    F_SUB = font(18)

    # ---- sheet 1: all DK renders ----
    cols, cw, ch = 5, 320, 470
    rows = (len(ROWS) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cw, rows * ch + 70), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    d.text((16, 18), "Duelist Kingdom (Season 1) character renders — GravekeepersBrewer", font=F_TITLE, fill=(240, 240, 245))
    d.text((16, 52), "deviantart.com/gravekeepersbrewer  ·  sprites now live in-game as the *_da sets", font=F_SUB, fill=(170, 175, 190))
    for i, (cid, name, _prev) in enumerate(ROWS):
        cx, cy = (i % cols) * cw, (i // cols) * ch + 70
        cell = checker(cw - 16, ch - 60)
        t = fit(load_front(f"{cid}_da"), (cw - 40, ch - 100))
        if t:
            cell.paste(t, ((cell.width - t.width) // 2, (cell.height - t.height) // 2), t)
        sheet.paste(cell, (cx + 8, cy + 6))
        d.text((cx + 10, cy + ch - 46), name, font=F_NAME, fill=(238, 238, 244))
    sheet.save(os.path.join(OUT, "DK_render_all.png"), optimize=True)
    print("wrote DK_render_all.png")

    # ---- sheet 2: previous look vs DK render ----
    cols = 3
    cw, ch = 460, 470
    rows = (len(ROWS) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cw, rows * ch + 70), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    d.text((16, 18), "New Duelist Kingdom render  vs  previous look", font=F_TITLE, fill=(240, 240, 245))
    d.text((16, 52), "left = previous in-game art (official set, or 'modular' if none)   ·   right = new DK render (*_da)", font=F_SUB, fill=(170, 175, 190))
    for i, (cid, name, prev) in enumerate(ROWS):
        cx, cy = (i % cols) * cw, (i // cols) * ch + 70
        half = (cw - 24) // 2
        # previous
        cellL = checker(half, ch - 70)
        if prev:
            t = fit(load_front(prev), (half - 24, ch - 120))
            if t:
                cellL.paste(t, ((cellL.width - t.width) // 2, (cellL.height - t.height) // 2), t)
        else:
            dl = ImageDraw.Draw(cellL)
            dl.text((12, cellL.height // 2 - 10), "modular avatar", font=F_SUB, fill=(200, 200, 200))
        # new
        cellR = checker(half, ch - 70)
        t = fit(load_front(f"{cid}_da"), (half - 24, ch - 120))
        if t:
            cellR.paste(t, ((cellR.width - t.width) // 2, (cellR.height - t.height) // 2), t)
        sheet.paste(cellL, (cx + 8, cy + 6))
        sheet.paste(cellR, (cx + 8 + half + 8, cy + 6))
        d.text((cx + 10, cy + ch - 52), name, font=F_NAME, fill=(238, 238, 244))
    sheet.save(os.path.join(OUT, "DK_render_vs_official.png"), optimize=True)
    print("wrote DK_render_vs_official.png")


if __name__ == "__main__":
    main()

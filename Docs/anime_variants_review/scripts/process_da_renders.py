#!/usr/bin/env python3
import os
from PIL import Image

BEST = {
  "yami_yugi": "yami_yugi_03.png",
  "joey": "joey_03.png",
  "tea": "tea_02.png",
  "tristan": "tristan_07.png",
  "bakura": "bakura_00.png",
  "yami_bakura": "yami_bakura_00.png",
}

SRC = "/home/greatsaiyadave/argon-avatar/research/anime_variants/da_renders"
DEST = "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/Assets/Resources/WRLDZ/AnimeCharacters"

def place_on_canvas(img, canvas=1024):
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    # crop to bbox
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    w,h = img.size
    canvas_img = Image.new("RGBA", (canvas, canvas), (0,0,0,0))
    # scale to fit: max dimension = canvas*0.9
    target = int(canvas * 0.92)
    if max(w,h) > target:
        if w > h:
            nw, nh = target, int(h * target/w)
        else:
            nw, nh = int(w * target/h), target
        img = img.resize((nw,nh), Image.LANCZOS)
    w,h = img.size
    # place feet near bottom: y = canvas - margin - h
    x = (canvas - w)//2
    y = canvas - int(canvas*0.08) - h
    canvas_img.paste(img, (x,y), img)
    return canvas_img

def main():
    for cid, fname in BEST.items():
        path = os.path.join(SRC, fname)
        if not os.path.exists(path):
            print("missing", path); continue
        im = Image.open(path)
        placed = place_on_canvas(im)
        outdir = os.path.join(DEST, cid + "_da")
        os.makedirs(outdir, exist_ok=True)
        # write the 12 sprites (all identical)
        for stem in [
            f"{cid}_da_front",
            f"{cid}_da_34",
            f"{cid}_da_side",
            f"{cid}_da_back",
            "expr_neutral","expr_serious","expr_smiling",
            "gesture_idle","gesture_draw","gesture_summon","gesture_attack","gesture_defeat",
        ]:
            outp = os.path.join(outdir, stem + ".png")
            placed.save(outp, optimize=True)
            # meta would be nice but not necessary; Unity will generate
        print(cid, "wrote 12 sprites to", outdir)

if __name__ == "__main__":
    main()

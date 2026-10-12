#!/usr/bin/env python3
"""pair.py out.png w f1 f2 ... : frames side by side (2 per row) at width w each, captioned with the file name; a 100 px grid
(ticks + labels in source pixels) is drawn on every frame so boxes can be read off."""
import sys, os
from PIL import Image, ImageDraw
out, w, fs = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = []
for f in fs:
    im = Image.open(f).convert('RGB'); sc = w / im.width
    t = im.resize((w, int(im.height * sc))); d = ImageDraw.Draw(t)
    for x in range(0, im.width, 100):
        d.line([(x * sc, 0), (x * sc, 6)], fill=(255, 0, 255)); d.text((x * sc + 1, 7), str(x), fill=(255, 0, 255))
    for y in range(0, im.height, 100):
        d.line([(0, y * sc), (6, y * sc)], fill=(255, 0, 255)); d.text((8, y * sc - 5), str(y), fill=(255, 0, 255))
    d.rectangle([0, t.height - 14, w, t.height], fill=(0, 0, 0)); d.text((3, t.height - 13), os.path.basename(f), fill=(255, 255, 120))
    ims.append(t)
cols = 2; rows = (len(ims) + 1) // 2; hh = max(i.height for i in ims)
sh = Image.new('RGB', (cols * w + 4, rows * (hh + 4)), (0, 0, 0))
for k, t in enumerate(ims): sh.paste(t, ((k % cols) * (w + 4), (k // cols) * (hh + 4)))
sh.save(out)

#!/usr/bin/env python3
"""montage.py out.png width f1 f2 ... : stack evidence images vertically, each scaled to the given width."""
import sys
from PIL import Image
out, w, fs = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
ims = []
for f in fs:
    im = Image.open(f); sc = w / im.width; ims.append(im.resize((w, int(im.height * sc))))
sh = Image.new('RGB', (w, sum(i.height + 4 for i in ims)), (0, 0, 0)); y = 0
for i in ims: sh.paste(i, (0, y)); y += i.height + 4
sh.save(out)

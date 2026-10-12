#!/usr/bin/env python3
"""zoomgrid.py out.png scale f:x0,y0,x1,y1:label ... : native crops scaled by `scale`, with a 20 px grid (source pixels,
labelled every 100) so lengths can be read off in source pixels. Writes only out.png."""
import sys
from PIL import Image, ImageDraw
out, sc = sys.argv[1], float(sys.argv[2]); tiles = []
for a in sys.argv[3:]:
    f, b, l = a.split(':'); x0, y0, x1, y1 = map(int, b.split(','))
    im = Image.open(f).convert('RGB').crop((x0, y0, x1, y1)); im = im.resize((int(im.width * sc), int(im.height * sc)), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    for x in range((x0 // 20 + 1) * 20, x1, 20):
        X = (x - x0) * sc; d.line([(X, 0), (X, im.height)], fill=(255, 0, 255) if x % 100 == 0 else (90, 0, 90), width=1)
        if x % 100 == 0: d.text((X + 2, 2), str(x), fill=(255, 255, 0))
    for y in range((y0 // 20 + 1) * 20, y1, 20):
        Y = (y - y0) * sc; d.line([(0, Y), (im.width, Y)], fill=(0, 255, 255) if y % 100 == 0 else (0, 80, 80), width=1)
        if y % 100 == 0: d.text((2, Y + 2), str(y), fill=(255, 255, 0))
    d.rectangle([0, im.height - 14, im.width, im.height], fill=(0, 0, 0)); d.text((3, im.height - 13), l, fill=(255, 255, 255))
    tiles.append(im)
W = sum(t.width + 4 for t in tiles); H = max(t.height for t in tiles)
s = Image.new('RGB', (W, H)); x = 0
for t in tiles: s.paste(t, (x, 0)); x += t.width + 4
s.save(out)

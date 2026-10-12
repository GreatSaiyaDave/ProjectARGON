"""grid1.py frame.png x0 y0 x1 y1 -> <frame>_g.png : that window at 1:1 with a 10 px grid, labels every 50 px (frame px)"""
import sys
from PIL import Image, ImageDraw
f = sys.argv[1]; x0, y0, x1, y1 = [int(v) for v in sys.argv[2:6]]
im = Image.open(f).convert('RGB').crop((x0, y0, x1, y1)); d = ImageDraw.Draw(im)
for gx in range((x0 // 10 + 1) * 10, x1, 10):
    d.line([(gx - x0, 0), (gx - x0, im.height)], fill=(255, 0, 0) if gx % 50 == 0 else (255, 170, 170), width=1)
    if gx % 50 == 0: d.text((gx - x0 + 2, 2), str(gx), fill=(255, 255, 0)); d.text((gx - x0 + 2, im.height - 12), str(gx), fill=(255, 255, 0))
for gy in range((y0 // 10 + 1) * 10, y1, 10):
    d.line([(0, gy - y0), (im.width, gy - y0)], fill=(255, 0, 0) if gy % 50 == 0 else (255, 170, 170), width=1)
    if gy % 50 == 0: d.text((2, gy - y0 + 2), str(gy), fill=(255, 255, 0)); d.text((im.width - 28, gy - y0 + 2), str(gy), fill=(255, 255, 0))
o = f.replace('.png', '_g.png'); im.save(o); print(o, im.size)

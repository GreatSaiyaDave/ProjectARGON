"""review.py <gest dir> [frames=0,4,8,12,16,20] -> review.png: one row per window (6 frames of its 2 s sheet, 213 px wide) + label"""
import sys, os, json
from PIL import Image, ImageDraw
d = sys.argv[1]; idx = [int(v) for v in (sys.argv[2] if len(sys.argv) > 2 else '0,4,8,12,16,20').split(',')]
j = json.load(open(os.path.join(d, 'windows.json'))); cw, ch = 213, 117
rows = []
for w in j['windows']:
    sh = Image.open(os.path.join(d, w['file']))
    row = Image.new('RGB', (len(idx) * (cw + 2) + 70, ch + 2), (20, 20, 25)); dr = ImageDraw.Draw(row)
    dr.text((2, 2), w['file'][:3], fill=(255, 230, 120)); dr.text((2, 16), f"{w['t0']:.0f}s", fill=(255, 230, 120)); dr.text((2, 30), f"f{w.get('face', 0)}", fill=(180, 180, 180))
    for k, fi in enumerate(idx):
        x0, y0 = (fi % 6) * 320, (fi // 6) * 188
        row.paste(sh.crop((x0, y0, x0 + 320, y0 + 176)).resize((cw, ch)), (70 + k * (cw + 2), 0))
    rows.append(row)
out = Image.new('RGB', (rows[0].width, sum(r.height for r in rows)), (0, 0, 0)); y = 0
for r in rows: out.paste(r, (0, y)); y += r.height
out.save(os.path.join(d, 'review.png')); print(os.path.join(d, 'review.png'), out.size, j['ep'][:50])

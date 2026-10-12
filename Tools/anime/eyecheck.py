"""eyecheck.py frame.png lx ly rx ry -> <frame>_check.png: 3x zooms (±60 px) round both guessed pupils with a crosshair and
a 20 px grid labelled as offsets from the guess, so a misread is corrected by a visible offset."""
import sys
from PIL import Image, ImageDraw
f = sys.argv[1]; lx, ly, rx, ry = [int(v) for v in sys.argv[2:6]]
im = Image.open(f).convert('RGB'); R = 60; Z = 3
out = Image.new('RGB', (2 * (2 * R * Z) + 20, 2 * R * Z + 20), (20, 20, 25)); d = ImageDraw.Draw(out)
for k, (cx, cy) in enumerate([(lx, ly), (rx, ry)]):
    crop = im.crop((cx - R, cy - R, cx + R, cy + R)).resize((2 * R * Z, 2 * R * Z), Image.NEAREST)
    ox = k * (2 * R * Z + 20); out.paste(crop, (ox, 20))
    c = R * Z
    for g in range(-R, R + 1, 20):
        col = (255, 255, 0) if g == 0 else (255, 120, 120)
        d.line([(ox + c + g * Z, 20), (ox + c + g * Z, 20 + 2 * R * Z)], fill=col, width=1)
        d.line([(ox, 20 + c + g * Z), (ox + 2 * R * Z, 20 + c + g * Z)], fill=col, width=1)
        d.text((ox + c + g * Z + 2, 22), f'{g:+d}', fill=(255, 255, 0)); d.text((ox + 2, 20 + c + g * Z + 2), f'{g:+d}', fill=(255, 255, 0))
    d.text((ox + 4, 4), f'{"L" if k == 0 else "R"} guess ({cx},{cy})  +x right, +y down', fill=(255, 255, 255))
o = f.replace('.png', '_check.png'); out.save(o); print(o)

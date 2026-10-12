#!/usr/bin/env python3
"""scan.py <ep> <t0> <t1> <step_s> <out_sheet.png> [cols] : contact sheet of 320x176 thumbnails every step_s seconds,
each captioned with its time in seconds. Writes only the sheet."""
import sys, subprocess, numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, __file__.rsplit('/', 1)[0]); from grab import path
ep, t0, t1, step, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
cols = int(sys.argv[6]) if len(sys.argv) > 6 else 8
W, H = 320, 176
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', str(t0), '-i', path(ep), '-t', str(t1 - t0), '-vf', f'fps=1/{step},scale={W}:{H}',
       '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
raw = subprocess.run(cmd, capture_output=True, check=True).stdout
n = len(raw) // (W * H * 3); fr = np.frombuffer(raw[:n * W * H * 3], np.uint8).reshape(n, H, W, 3)
rows = (n + cols - 1) // cols
sh = Image.new('RGB', (cols * W, rows * (H + 14)), (15, 15, 20)); d = ImageDraw.Draw(sh)
for i in range(n):
    x, y = (i % cols) * W, (i // cols) * (H + 14)
    sh.paste(Image.fromarray(fr[i]), (x, y)); d.text((x + 3, y + H + 1), f'e{ep} t={t0 + i * step:.0f}', fill=(255, 255, 120))
sh.save(out); print(out, n, 'frames')

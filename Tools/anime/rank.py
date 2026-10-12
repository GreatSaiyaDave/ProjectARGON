#!/usr/bin/env python3
"""rank.py <ep> <t0> <t1> <outdir> name=h0:h1,s0:s1,v0:v1 [...] : decode the episode at 1 fps (640x352), score every frame
per named HSV window by the area of its largest flat connected blob, write <outdir>/<name>_e<ep>.png = the 24 best frames
captioned with time and area, and <outdir>/rank_e<ep>.json with all scores. Writes only into <outdir>."""
import sys, os, json, subprocess, numpy as np, cv2
from PIL import Image, ImageDraw
sys.path.insert(0, __file__.rsplit('/', 1)[0]); from grab import path
ep, t0, t1, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
wins = {}
for a in sys.argv[5:]:
    n, spec = a.split('='); hh, ss, vv = spec.split(',')
    wins[n] = tuple(tuple(float(x) for x in q.split(':')) for q in (hh, ss, vv))
os.makedirs(out, exist_ok=True)
W, H = 640, 352
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', str(t0), '-i', path(ep), '-t', str(t1 - t0), '-vf', f'fps=1,scale={W}:{H}',
       '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8)
scores = {n: [] for n in wins}; thumbs = {}; i = 0
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    fr = np.frombuffer(raw, np.uint8).reshape(H, W, 3); t = t0 + i; i += 1
    hsv = cv2.cvtColor(fr, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    h = hsv[..., 0] * 360 / 256; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
    keepT = False
    for n, ((h0, h1), (s0, s1), (v0, v1)) in wins.items():
        hm = ((h >= h0) & (h <= h1)) if h0 <= h1 else ((h >= h0) | (h <= h1))
        m = (hm & (s >= s0) & (s <= s1) & (v >= v0) & (v <= v1)).astype(np.uint8)
        k, lab, st, ce = cv2.connectedComponentsWithStats(m, 8)
        a = int(st[1:, cv2.CC_STAT_AREA].max()) if k > 1 else 0
        scores[n].append((a, t))
    thumbs[t] = cv2.resize(fr, (320, 176), interpolation=cv2.INTER_AREA)
p.stdout.close(); p.wait()
json.dump({n: sorted(v, reverse=True)[:200] for n, v in scores.items()}, open(os.path.join(out, f'rank_e{ep}.json'), 'w'))
for n, v in scores.items():
    top = sorted(v, reverse=True)
    # de-duplicate: keep frames at least 3 s apart
    pick = []
    for a, t in top:
        if all(abs(t - q) >= 3 for _, q in pick): pick.append((a, t))
        if len(pick) == 24: break
    cols = 6; rows = (len(pick) + cols - 1) // cols
    sh = Image.new('RGB', (cols * 320, max(1, rows) * 190), (15, 15, 20)); d = ImageDraw.Draw(sh)
    for k, (a, t) in enumerate(pick):
        x, y = (k % cols) * 320, (k // cols) * 190
        sh.paste(Image.fromarray(thumbs[t]), (x, y)); d.text((x + 3, y + 177), f'{n} e{ep} t={t:.0f} a={a}', fill=(255, 255, 120))
    sh.save(os.path.join(out, f'{n}_e{ep}.png'))
print('frames', i, {n: [t for a, t in sorted(v, reverse=True)[:6]] for n, v in scores.items()})

"""refine.py frame.png lx ly rx ry [r=70] -> refined pupil centres near the guesses: in a window round each guess, the
sclera = brightest low-saturation blob, the iris = the largest dark/saturated blob touching it, the pupil = the darkest
quarter of the iris. Writes <frame>_ref.png (zooms with the result marked)."""
import sys, os, cv2, numpy as np
from PIL import Image, ImageDraw
f = sys.argv[1]; g = [int(v) for v in sys.argv[2:6]]; R = int(sys.argv[6]) if len(sys.argv) > 6 else 70
im = Image.open(f).convert('RGB'); a = np.array(im); H, W = a.shape[:2]
hsv = cv2.cvtColor(a, cv2.COLOR_RGB2HSV).astype(int); v = hsv[..., 2]; s = hsv[..., 1]
def refine(cx, cy):
    y0, y1, x0, x1 = max(0, cy - R), min(H, cy + R), max(0, cx - R), min(W, cx + R)
    vv, ss = v[y0:y1, x0:x1], s[y0:y1, x0:x1]
    thr = min(170, int(np.percentile(vv, 97)) - 15)   # a dim frame: the sclera is its brightest patch
    scl = ((vv > thr) & (ss < 70)).astype(np.uint8)
    n, lab, st, ce = cv2.connectedComponentsWithStats(scl, 8)
    best = None
    for i in range(1, n):
        ar = st[i, cv2.CC_STAT_AREA]
        if ar < 25: continue
        d = np.hypot(ce[i][0] - (cx - x0), ce[i][1] - (cy - y0))
        sc = ar / (1 + d / 20)
        if best is None or sc > best[0]: best = (sc, i)
    if best is None: return None
    i = best[1]; xs, ys, ws, hs = st[i, 0], st[i, 1], st[i, 2], st[i, 3]
    X0, Y0, X1, Y1 = max(0, xs - ws // 2), max(0, ys - hs // 3), min(x1 - x0, xs + ws + ws // 2), min(y1 - y0, ys + hs + hs // 3)
    dark = ((vv < max(110, thr - 50)) | ((ss > 70) & (vv < 215)))[Y0:Y1, X0:X1].astype(np.uint8)
    n2, lab2, st2, ce2 = cv2.connectedComponentsWithStats(dark, 8)
    # the iris blob: touches or overlaps the sclera box and is the largest such
    bj = None
    for j in range(1, n2):
        ar = st2[j, cv2.CC_STAT_AREA]
        if ar < 12: continue
        if bj is None or ar > bj[0]: bj = (ar, j)
    if bj is None: return None
    j = bj[1]; ys2, xs2 = np.where(lab2 == j)
    val = vv[Y0:Y1, X0:X1][ys2, xs2]; k = max(5, len(val) // 4); idx = np.argsort(val)[:k]
    return int(round(x0 + X0 + xs2[idx].mean())), int(round(y0 + Y0 + ys2[idx].mean()))
L = refine(g[0], g[1]); Rr = refine(g[2], g[3])
print(os.path.basename(f), 'L', L, 'R', Rr)
Z = 3; out = Image.new('RGB', (2 * (2 * R * Z) + 20, 2 * R * Z + 20), (20, 20, 25)); d = ImageDraw.Draw(out)
for k, (gc, rc) in enumerate([((g[0], g[1]), L), ((g[2], g[3]), Rr)]):
    crop = im.crop((gc[0] - R, gc[1] - R, gc[0] + R, gc[1] + R)).resize((2 * R * Z, 2 * R * Z), Image.NEAREST)
    ox = k * (2 * R * Z + 20); out.paste(crop, (ox, 20))
    if rc:
        px, py = ox + (rc[0] - gc[0] + R) * Z, 20 + (rc[1] - gc[1] + R) * Z
        d.ellipse((px - 12, py - 12, px + 12, py + 12), outline=(0, 255, 0), width=3); d.line([(px - 25, py), (px + 25, py)], fill=(0, 255, 0), width=2); d.line([(px, py - 25), (px, py + 25)], fill=(0, 255, 0), width=2)
d.text((4, 4), f'{os.path.basename(f)} L{L} R{Rr}', fill=(255, 255, 255)); out.save(f.replace('.png', '_ref.png'))

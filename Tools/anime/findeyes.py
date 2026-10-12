"""findeyes.py frame.png [frame2 ...] -> prints '<frame> lx ly rx ry' and writes <frame>_auto.png (the two eyes zoomed with
the found pupils marked). Anime eyes = a white sclera patch touching a dark/saturated iris patch; the two best side-by-side
pairs on one line are the eyes, the pupil = the darkest 25% of the iris patch's centroid."""
import sys, os, cv2, numpy as np
from PIL import Image, ImageDraw
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
def find(path):
    im = Image.open(path).convert('RGB'); a = np.array(im); H, W = a.shape[:2]
    gray = cv2.cvtColor(a, cv2.COLOR_RGB2GRAY)
    faces = sorted(casc.detectMultiScale(cv2.equalizeHist(gray), 1.1, 4, minSize=(90, 90)), key=lambda b: -b[2])
    if len(faces): x, y, w, h = faces[0]; band = (max(0, y + int(0.15 * h)), min(H, y + int(0.8 * h)), max(0, x - w // 8), min(W, x + w + w // 8))
    else: band = (0, H, 0, W)
    hsv = cv2.cvtColor(a, cv2.COLOR_RGB2HSV).astype(int)
    v = hsv[..., 2]; s = hsv[..., 1]
    sclera = (v > 200) & (s < 45)
    dark = v < 90
    sat = (s > 60) & (v < 215) & (v > 40)
    eyeish = (sclera | dark | sat).astype(np.uint8)
    y0, y1, x0, x1 = band
    mask = np.zeros_like(eyeish); mask[y0:y1, x0:x1] = 1; eyeish &= mask
    # sclera components with an iris neighbour
    n, lab, stats, cent = cv2.connectedComponentsWithStats(sclera.astype(np.uint8) * mask, 8)
    cands = []
    for i in range(1, n):
        area = stats[i, cv2.CC_STAT_AREA]
        if area < 40 or area > 0.02 * H * W: continue
        cx, cy = cent[i]; xs, ys, ws, hs = stats[i, 0], stats[i, 1], stats[i, 2], stats[i, 3]
        # grow the sclera box a bit: the iris sits inside / beside it
        X0, Y0, X1, Y1 = max(0, xs - ws // 2), max(0, ys - hs // 4), min(W, xs + ws + ws // 2), min(H, ys + hs + hs // 4)
        sub = (dark | sat)[Y0:Y1, X0:X1]
        if sub.sum() < 20: continue
        # the iris: the largest dark/saturated blob touching the sclera box
        n2, lab2, st2, ce2 = cv2.connectedComponentsWithStats(sub.astype(np.uint8), 8)
        best = None
        for j in range(1, n2):
            ar = st2[j, cv2.CC_STAT_AREA]
            if ar < 15: continue
            if best is None or ar > best[0]: best = (ar, j)
        if best is None: continue
        j = best[1]; ys2, xs2 = np.where(lab2 == j)
        vv = v[Y0:Y1, X0:X1][ys2, xs2]; k = max(5, len(vv) // 4); idx = np.argsort(vv)[:k]
        px, py = X0 + xs2[idx].mean(), Y0 + ys2[idx].mean()
        cands.append((area + best[0], px, py, area))
    cands.sort(key=lambda c: -c[0])
    best = None
    for i in range(len(cands)):
        for j in range(i + 1, len(cands)):
            (sa, ax, ay, _), (sb, bx, by, _) = cands[i], cands[j]
            dx, dy = abs(ax - bx), abs(ay - by)
            if dx < 40 or dy > 0.35 * dx: continue
            score = sa + sb - 2 * dy
            if best is None or score > best[0]: best = (score, (ax, ay), (bx, by))
    if best is None: return None
    (ax, ay), (bx, by) = sorted([best[1], best[2]])
    return int(round(ax)), int(round(ay)), int(round(bx)), int(round(by))
for f in sys.argv[1:]:
    r = find(f)
    if r is None: print(f, 'NONE'); continue
    lx, ly, rx, ry = r; print(f, lx, ly, rx, ry)
    im = Image.open(f).convert('RGB'); R = 70; Z = 3
    out = Image.new('RGB', (2 * (2 * R * Z) + 20, 2 * R * Z + 20), (20, 20, 25)); d = ImageDraw.Draw(out)
    for k, (cx, cy) in enumerate([(lx, ly), (rx, ry)]):
        crop = im.crop((cx - R, cy - R, cx + R, cy + R)).resize((2 * R * Z, 2 * R * Z), Image.NEAREST)
        ox = k * (2 * R * Z + 20); out.paste(crop, (ox, 20)); c = R * Z
        d.line([(ox + c - 30, 20 + c), (ox + c + 30, 20 + c)], fill=(0, 255, 0), width=2); d.line([(ox + c, 20 + c - 30), (ox + c, 20 + c + 30)], fill=(0, 255, 0), width=2)
        d.ellipse((ox + c - 15, 20 + c - 15, ox + c + 15, 20 + c + 15), outline=(0, 255, 0), width=2)
    d.text((4, 4), f'{os.path.basename(f)}  L({lx},{ly}) R({rx},{ry})', fill=(255, 255, 255))
    out.save(f.replace('.png', '_auto.png'))

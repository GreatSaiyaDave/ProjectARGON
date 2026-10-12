#!/usr/bin/env python3
"""meas.py <jobs.json> <results.json> <evidence_dir> : class-filtered lit / shade fills (this measurer's own sampler).

Per job: frame (path), box (x0,y0,x1,y1), h (deg, h0>h1 wraps through 0), s, v (0..1) windows, label.
 1. flat filter: every channel within 14 of all 8 neighbours (drops line art, anti-aliasing, gradients' edges);
 2. class filter: HSV window;
 3. tones: 1-D 2-means on V of the kept pixels; if the two centres are < 0.07 apart in V, or the smaller cluster is under 6 %
    of the kept pixels, one tone is reported ('one'). Each tone's per-channel MEDIAN is the fill, with its pixel count.
 3b. modes: the kept pixels quantised to 12-level bins; the 3 most common bins with their medians (the flat base fill).
 4. optional 'exclude' boxes (list of x0,y0,x1,y1) inside the box are dropped before step 1 results are counted.
Evidence: <evidence_dir>/<label>.png = the crop (left) and the classification (right: lit kept pixels in their own colour,
shade kept pixels in their own colour with a cyan tick grid, rejected pixels dimmed grey)."""
import sys, json, os
import numpy as np
from PIL import Image, ImageDraw

def hsv(a8):
    a = a8.astype(np.float64) / 255.0
    mx = a.max(axis=2); mn = a.min(axis=2); d = mx - mn
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx); m = d > 1e-9
    rm = m & (mx == r); gm = m & (mx == g) & ~rm; bm = m & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6; h[gm] = (b - r)[gm] / d[gm] + 2; h[bm] = (r - g)[bm] / d[bm] + 4
    return h * 60.0, np.where(mx > 1e-9, d / np.maximum(mx, 1e-9), 0.0), mx

def run(job, evdir):
    im = np.asarray(Image.open(job['frame']).convert('RGB')).astype(np.int16)
    x0, y0, x1, y1 = job['box']
    pad = im[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1]
    c = pad[1:-1, 1:-1]
    flat = np.ones(c.shape[:2], bool)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx == 0 and dy == 0: continue
            nb = pad[1 + dy:pad.shape[0] - 1 + dy, 1 + dx:pad.shape[1] - 1 + dx]
            flat &= (np.abs(c - nb).max(axis=2) < job.get('flat', 14))
    h, s, v = hsv(c.astype(np.uint8))
    h0, h1 = job['h']
    hm = ((h >= h0) & (h <= h1)) if h0 <= h1 else ((h >= h0) | (h <= h1))
    keep = flat & hm & (s >= job['s'][0]) & (s <= job['s'][1]) & (v >= job['v'][0]) & (v <= job['v'][1])
    for ex in job.get('exclude', []):
        ex0, ey0, ex1, ey1 = ex
        keep[max(0, ey0 - y0):max(0, ey1 - y0), max(0, ex0 - x0):max(0, ex1 - x0)] = False
    px = c[keep]; vv = v[keep]
    out = dict(label=job['label'], frame=job['frame'], box=job['box'], h=job['h'], s=job['s'], v=job['v'],
               kept=int(keep.sum()), area=int(keep.size), fills=[])
    lit_mask = np.zeros_like(keep); shade_mask = np.zeros_like(keep)
    if len(px) >= 20:
        lo, hi = float(np.percentile(vv, 10)), float(np.percentile(vv, 90))
        for _ in range(60):
            t = (lo + hi) / 2; a, b = vv[vv < t], vv[vv >= t]
            if len(a) == 0 or len(b) == 0: break
            nlo, nhi = float(a.mean()), float(b.mean())
            if abs(nlo - lo) < 1e-7 and abs(nhi - hi) < 1e-7: break
            lo, hi = nlo, nhi
        t = (lo + hi) / 2
        L = vv >= t
        if hi - lo < 0.07 or min(L.sum(), (~L).sum()) < 0.06 * len(px):
            groups = [('one', np.ones(len(px), bool))]
        else:
            groups = [('lit', L), ('shade', ~L)]
        for name, g in groups:
            med = np.median(px[g], axis=0).round().astype(int)
            iq = np.percentile(px[g], [25, 75], axis=0).round().astype(int)
            out['fills'].append(dict(tone=name, hex='#%02X%02X%02X' % tuple(med), n=int(g.sum()),
                                     iqr=['#%02X%02X%02X' % tuple(iq[0]), '#%02X%02X%02X' % tuple(iq[1])]))
        # modes: kept pixels quantised to 12-level bins per channel; the 3 largest bins, each with its own median
        q = (px // 12).astype(np.int32); key = q[:, 0] * 10000 + q[:, 1] * 100 + q[:, 2]
        uk, inv, cnt = np.unique(key, return_inverse=True, return_counts=True)
        out['modes'] = []
        for j in np.argsort(-cnt)[:3]:
            med = np.median(px[inv == j], axis=0).round().astype(int)
            out['modes'].append(dict(hex='#%02X%02X%02X' % tuple(med), n=int(cnt[j])))
        idx = np.argwhere(keep)
        for name, g in groups:
            sel = idx[g]
            (lit_mask if name in ('lit', 'one') else shade_mask)[sel[:, 0], sel[:, 1]] = True
    # evidence image
    crop = c.astype(np.uint8)
    vis = (crop.astype(np.float32) * 0.18 + 40).astype(np.uint8)
    vis[lit_mask] = crop[lit_mask]
    vis[shade_mask] = crop[shade_mask]
    yy, xx = np.nonzero(shade_mask)
    tick = ((yy + xx) % 6 == 0)
    vis[yy[tick], xx[tick]] = (0, 255, 255)
    H, W = crop.shape[:2]
    sc = min(1.0, 520.0 / max(H, W)) if max(H, W) > 520 else min(3.0, 260.0 / max(H, W))
    def rs(a): return Image.fromarray(a).resize((max(1, int(W * sc)), max(1, int(H * sc))), Image.NEAREST)
    A, B = rs(crop), rs(vis)
    sheet = Image.new('RGB', (A.width * 2 + 6, A.height + 40), (10, 10, 10))
    sheet.paste(A, (0, 0)); sheet.paste(B, (A.width + 6, 0))
    d = ImageDraw.Draw(sheet)
    d.text((3, A.height + 2), f"{job['label']}  {os.path.basename(job['frame'])} box={tuple(job['box'])}", fill=(255, 255, 120))
    d.text((3, A.height + 16), '  '.join(f"{f['tone']} {f['hex']} n={f['n']}" for f in out['fills']) + f"  kept {out['kept']}/{out['area']}", fill=(255, 255, 255))
    x = 3
    for f in out['fills']:
        rgb = tuple(int(f['hex'][i:i + 2], 16) for i in (1, 3, 5))
        d.rectangle([sheet.width - 60 - x, A.height + 2, sheet.width - 30 - x, A.height + 36], fill=rgb, outline=(255, 255, 255)); x += 34
    os.makedirs(evdir, exist_ok=True)
    out['evidence'] = os.path.join(evdir, job['label'] + '.png'); sheet.save(out['evidence'])
    return out

if __name__ == '__main__':
    jobs = json.load(open(sys.argv[1])); res = [run(j, sys.argv[3]) for j in jobs]
    old = []
    if os.path.exists(sys.argv[2]):
        old = [r for r in json.load(open(sys.argv[2])) if r['label'] not in {j['label'] for j in jobs}]
    json.dump(old + res, open(sys.argv[2], 'w'), indent=1)
    for r in res:
        print(f"{r['label']:<34} {os.path.basename(r['frame']):<24} box={tuple(r['box'])} kept={r['kept']:>6}/{r['area']:<7} " +
              ' | '.join(f"{f['tone']} {f['hex']} n={f['n']}" for f in r['fills']) + '   modes ' + ' '.join(f"{m['hex']}:{m['n']}" for m in r.get('modes', [])))

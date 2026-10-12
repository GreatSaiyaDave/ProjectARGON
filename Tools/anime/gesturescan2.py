"""gesturescan2.py "<episode substring>" outdir [top=14] — gesture moments without needing a face: a held camera (few moving
pixels) with the motion concentrated in one blob the size of an arm/torso (a character gesturing). Faces are a bonus.
Writes windows.json + one 12 fps contact sheet (2 s, 6x4) per window."""
import sys, os, json, subprocess, numpy as np, cv2
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, out = sys.argv[1], sys.argv[2]; top = int(sys.argv[3]) if len(sys.argv) > 3 else 14
files = [f for f in sorted(os.listdir(EP)) if sub in f]; assert files, 'no episode ' + sub
path = os.path.join(EP, files[0]); os.makedirs(out, exist_ok=True)
W, H, FPS = 640, 352, 6
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-i', path, '-vf', f'fps={FPS},scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'bgr24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8)
prev = None; i = 0; rec = []; face = 0
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    fr = np.frombuffer(raw, np.uint8).reshape(H, W, 3); t = i / FPS
    gray = cv2.cvtColor(fr, cv2.COLOR_BGR2GRAY)
    if i % 3 == 0:
        fs = casc.detectMultiScale(cv2.equalizeHist(gray), scaleFactor=1.1, minNeighbors=4, minSize=(28, 28), maxSize=(200, 200))
        face = int(max(fs, key=lambda f: f[2])[2]) if len(fs) else 0
    gm = mf = blob = conc = bh = 0.0
    if prev is not None:
        d = cv2.absdiff(gray, prev); gm = float(d.mean())
        m = (d > 22).astype(np.uint8); mf = float(m.mean())
        if 0.004 < mf < 0.30:
            m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
            n, lab, st, ce = cv2.connectedComponentsWithStats(m, 8)
            if n > 1:
                j = 1 + st[1:, cv2.CC_STAT_AREA].argmax(); a = st[j, cv2.CC_STAT_AREA]
                blob = a / (W * H); conc = a / max(1, m.sum()); bh = st[j, cv2.CC_STAT_HEIGHT] / H
    rec.append((t, face, gm, mf, blob, conc, bh)); prev = gray; i += 1
p.stdout.close(); p.wait()
L = int(2.5 * FPS); cands = []
for s in range(0, len(rec) - L, 3):
    win = rec[s:s + L]
    gms = np.array([r[2] for r in win]); mfs = np.array([r[3] for r in win])
    if gms.max() > 40: continue                       # a cut inside the window
    if np.mean(mfs > 0.30) > 0.15: continue           # pans / zooms / full-frame effects
    blobs = np.array([r[4] for r in win]); concs = np.array([r[5] for r in win]); bhs = np.array([r[6] for r in win])
    moving = blobs > 0.006                            # frames with a gesture-sized moving blob (>= 0.6 % of the frame)
    if moving.sum() < 4: continue
    if np.mean(bhs[moving]) < 0.12: continue          # the blob spans at least an arm's length of the frame
    score = float((blobs * concs)[moving].sum()) * (1.0 + 0.5 * (max(r[1] for r in win) > 0))
    cands.append((score, win[0][0], float(blobs.max()), float(np.mean(concs[moving])), int(max(r[1] for r in win))))
cands.sort(key=lambda c: -c[0]); picked = []
for c in cands:
    if all(abs(c[1] - q[1]) > 3.0 for q in picked): picked.append(c)
    if len(picked) >= top: break
res = []
for k, (score, t0, blob, conc, face) in enumerate(sorted(picked, key=lambda c: c[1])):
    cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', f'{t0:.3f}', '-i', path, '-t', '2.0', '-vf', 'fps=12,scale=320:176', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
    raw = subprocess.run(cmd, capture_output=True).stdout; n = len(raw) // (320 * 176 * 3)
    sheet = Image.new('RGB', (6 * 320, 4 * (176 + 12)), (20, 20, 25)); d = ImageDraw.Draw(sheet)
    for j in range(min(24, n)):
        im = Image.frombytes('RGB', (320, 176), raw[j * 320 * 176 * 3:(j + 1) * 320 * 176 * 3])
        x, y = (j % 6) * 320, (j // 6) * (176 + 12); sheet.paste(im, (x, y)); d.text((x + 2, y + 176), f'{t0 + j / 12:.2f}s', fill=(230, 230, 230))
    name = f'w{k:02d}_{t0:07.1f}s.png'; sheet.save(os.path.join(out, name))
    res.append({'file': name, 't0': round(t0, 2), 'score': round(score, 3), 'blob': round(blob, 3), 'conc': round(conc, 2), 'face': face})
json.dump({'ep': files[0], 'windows': res}, open(os.path.join(out, 'windows.json'), 'w'), indent=0)
print(f'{files[0][:44]}: {i} frames, {len(cands)} candidates, {len(res)} sheets')

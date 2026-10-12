"""gesturescan.py "<episode substring>" outdir [top=12] — find gesture moments: a steady face (anime cascade) with a lot of
motion in the body region below it while the camera holds. Writes windows.json + one 12 fps contact sheet (2 s, 6x4) per window."""
import sys, os, json, subprocess, numpy as np, cv2
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, out = sys.argv[1], sys.argv[2]; top = int(sys.argv[3]) if len(sys.argv) > 3 else 12
files = [f for f in sorted(os.listdir(EP)) if sub in f]; assert files, 'no episode ' + sub
path = os.path.join(EP, files[0]); os.makedirs(out, exist_ok=True)
W, H, FPS = 640, 352, 6
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-i', path, '-vf', f'fps={FPS},scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'bgr24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8)
prev = None; i = 0; rec = []   # per frame: t, face(x,y,w,h)|None, body motion, global motion
face = None
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    fr = np.frombuffer(raw, np.uint8).reshape(H, W, 3); t = i / FPS
    gray = cv2.cvtColor(fr, cv2.COLOR_BGR2GRAY)
    if i % 3 == 0:
        fs = casc.detectMultiScale(cv2.equalizeHist(gray), scaleFactor=1.1, minNeighbors=4, minSize=(36, 36), maxSize=(170, 170))
        face = max(fs, key=lambda f: f[2]) if len(fs) else None
    bm = gm = 0.0
    if prev is not None:
        d = cv2.absdiff(gray, prev).astype(np.float32)
        gm = float(d.mean())
        if face is not None:
            x, y, w, h = face
            x0, x1 = max(0, int(x - 1.5 * w)), min(W, int(x + 2.5 * w)); y0 = min(H - 1, int(y + h))
            if y0 + 10 < H and x1 > x0: bm = float(d[y0:H, x0:x1].mean())
    rec.append((t, None if face is None else [int(v) for v in face], bm, gm)); prev = gray; i += 1
p.stdout.close(); p.wait()
# score 2.5 s windows: face present in >= 80 % of frames, camera held (global motion not dominated by body), body moves
L = int(2.5 * FPS); cands = []
for s in range(0, len(rec) - L, 3):
    win = rec[s:s + L]; nf = sum(1 for r in win if r[1] is not None)
    if nf < 0.8 * L: continue
    gms = np.array([r[3] for r in win]); bms = np.array([r[2] for r in win])
    if gms.max() > 40: continue                     # shot cut inside the window
    # a face that drifts > 1 face-width is a pan/zoom, not a held shot
    fx = [r[1][0] + r[1][2] / 2 for r in win if r[1]]; fw = np.mean([r[1][2] for r in win if r[1]])
    if max(fx) - min(fx) > 1.2 * fw: continue
    score = bms.mean() / (gms.mean() + 1.0) * min(1.0, fw / 60)   # mid shots (bigger faces) weigh more
    if bms.mean() < 4: continue
    cands.append((score, win[0][0], float(bms.mean()), float(gms.mean()), [int(v) for v in np.median([r[1] for r in win if r[1]], 0)]))
cands.sort(key=lambda c: -c[0]); picked = []
for c in cands:
    if all(abs(c[1] - q[1]) > 3.0 for q in picked): picked.append(c)
    if len(picked) >= top: break
# contact sheets: 2 s at 12 fps from the window start, 6x4 cells of 320x176
res = []
for k, (score, t0, bm, gm, fb) in enumerate(sorted(picked, key=lambda c: c[1])):
    cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', f'{t0:.3f}', '-i', path, '-t', '2.0', '-vf', 'fps=12,scale=320:176', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
    raw = subprocess.run(cmd, capture_output=True).stdout; n = len(raw) // (320 * 176 * 3)
    sheet = Image.new('RGB', (6 * 320, 4 * (176 + 12)), (20, 20, 25)); d = ImageDraw.Draw(sheet)
    for j in range(min(24, n)):
        im = Image.frombytes('RGB', (320, 176), raw[j * 320 * 176 * 3:(j + 1) * 320 * 176 * 3])
        x, y = (j % 6) * 320, (j // 6) * (176 + 12); sheet.paste(im, (x, y)); d.text((x + 2, y + 176), f'{t0 + j / 12:.2f}s', fill=(230, 230, 230))
    name = f'w{k:02d}_{t0:07.1f}s.png'; sheet.save(os.path.join(out, name))
    res.append({'file': name, 't0': round(t0, 2), 'score': round(score, 2), 'body': round(bm, 1), 'cam': round(gm, 1), 'face': fb})
json.dump({'ep': files[0], 'windows': res}, open(os.path.join(out, 'windows.json'), 'w'), indent=0)
print(f'{files[0][:44]}: {i} frames, {len(cands)} candidates, {len(res)} sheets')

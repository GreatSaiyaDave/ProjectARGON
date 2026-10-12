"""Stream an episode through ffmpeg, detect anime faces, save crops >= min px with a sheet.
usage: harvest.py "<episode substring>" <start_s> <dur_s> <fps> <outdir> [min_face=140]"""
import sys, os, json, subprocess, glob
import numpy as np, cv2
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, start, dur, fps, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
minf = int(sys.argv[6]) if len(sys.argv) > 6 else 140
files = [f for f in sorted(os.listdir(EP)) if sub in f]
assert files, 'no episode matches ' + sub
path = os.path.join(EP, files[0]); os.makedirs(out, exist_ok=True)
W, H = 1280, 704
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', str(start), '-i', path, '-t', str(dur), '-vf', f'fps={fps},scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'bgr24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8)
idx = []; i = 0; crops = []
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    frame = np.frombuffer(raw, np.uint8).reshape(H, W, 3)
    t = start + i / fps; i += 1
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY); gray = cv2.equalizeHist(gray)
    faces = casc.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(minf, minf))
    for (x, y, w, h) in faces:
        # widen the box: the cascade hugs the eyes/nose, hair and chin sit outside
        cx, cy = x + w / 2, y + h / 2; s = int(w * 1.5)
        x0, y0 = int(max(0, cx - s / 2)), int(max(0, cy - s * 0.55)); x1, y1 = min(W, x0 + s), min(H, y0 + s)
        crop = frame[y0:y1, x0:x1]
        name = f'{os.path.basename(files[0])[:28].replace(" ", "_")}_{t:07.1f}_{x}_{y}_{w}.png'
        cv2.imwrite(os.path.join(out, name), crop)
        idx.append({'file': name, 'ep': files[0], 't': round(t, 2), 'box': [int(x), int(y), int(w), int(h)]})
p.stdout.close(); p.wait()
json.dump(idx, open(os.path.join(out, 'index.json'), 'w'))
# sheet: largest first
idx.sort(key=lambda r: -r['box'][2])
cell = 160; cols = 12; rows = (len(idx) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell, max(1, rows) * (cell + 14)), (20, 20, 25)); d = ImageDraw.Draw(sheet)
for k, r in enumerate(idx):
    im = Image.open(os.path.join(out, r['file'])).convert('RGB'); im.thumbnail((cell, cell))
    x, y = (k % cols) * cell, (k // cols) * (cell + 14)
    sheet.paste(im, (x, y)); d.text((x + 2, y + cell), f"{r['t']:.0f}s {r['box'][2]}px", fill=(230, 230, 230))
sheet.save(os.path.join(out, 'sheet.png'))
print(f'{files[0][:40]}: {i} frames, {len(idx)} faces >= {minf}px, sheet {sheet.size}')

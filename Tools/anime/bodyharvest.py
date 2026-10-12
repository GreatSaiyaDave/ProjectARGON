"""bodyharvest.py "<episode substring>" start dur fps outdir [min_face=70] [max_face=260] — like harvest.py but keeps the
BODY under each detected face (3.6 face heights down, 1 face width each side): outfit references. sheet.png sorted by face size."""
import sys, os, json, subprocess
import numpy as np, cv2
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, start, dur, fps, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
minf = int(sys.argv[6]) if len(sys.argv) > 6 else 70; maxf = int(sys.argv[7]) if len(sys.argv) > 7 else 260
files = [f for f in sorted(os.listdir(EP)) if sub in f]; assert files, 'no episode matches ' + sub
path = os.path.join(EP, files[0]); os.makedirs(out, exist_ok=True)
W, H = 1280, 704
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', str(start), '-i', path, '-t', str(dur), '-vf', f'fps={fps},scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'bgr24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8)
idx = []; i = 0
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    frame = np.frombuffer(raw, np.uint8).reshape(H, W, 3); t = start + i / fps; i += 1
    gray = cv2.equalizeHist(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY))
    for (x, y, w, h) in casc.detectMultiScale(gray, 1.1, 5, minSize=(minf, minf)):
        if w > maxf: continue
        x0, y0 = int(max(0, x - w)), int(max(0, y - 0.4 * h)); x1, y1 = int(min(W, x + 2 * w)), int(min(H, y + 3.6 * h))
        if y1 - y0 < 2.5 * h: continue   # the body is cut off by the frame edge
        name = f'{t:07.1f}_{x}_{y}_{w}.png'
        cv2.imwrite(os.path.join(out, name), frame[y0:y1, x0:x1])
        idx.append({'file': name, 'ep': files[0], 't': round(t, 2), 'box': [int(x), int(y), int(w), int(h)]})
p.stdout.close(); p.wait()
json.dump(idx, open(os.path.join(out, 'index.json'), 'w'))
idx.sort(key=lambda r: -r['box'][2])
cell = (150, 300); cols = 12; rows = (len(idx) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell[0], max(1, rows) * (cell[1] + 14)), (20, 20, 25)); d = ImageDraw.Draw(sheet)
for k, r in enumerate(idx):
    im = Image.open(os.path.join(out, r['file'])).convert('RGB'); im.thumbnail(cell)
    x, y = (k % cols) * cell[0], (k // cols) * (cell[1] + 14)
    sheet.paste(im, (x, y)); d.text((x + 2, y + cell[1]), f"{r['t']:.0f}s {r['box'][2]}px", fill=(230, 230, 230))
sheet.save(os.path.join(out, 'sheet.png'))
print(f'{files[0][:40]}: {i} frames, {len(idx)} bodies, sheet {sheet.size}')

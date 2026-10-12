"""hairscan.py "<episode substring>" t0 t1 r,g,b tol outdir [skip_r,g,b] -> ranks 1 fps frames by the largest blob of that colour
(a character's hair / hat), saves the top 30 full frames + sheet.png with times. Blob area ~ how close the shot is."""
import sys, os, subprocess, numpy as np, cv2
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, t0, t1 = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]); col = np.array([int(v) for v in sys.argv[4].split(',')]); tol = int(sys.argv[5]); out = sys.argv[6]
files = [f for f in sorted(os.listdir(EP)) if sub in f]; assert files, sub
path = os.path.join(EP, files[0]); os.makedirs(out, exist_ok=True)
W, H = 640, 352
cmd = ['ffmpeg', '-nostdin', '-v', 'error', '-ss', str(t0), '-i', path, '-t', str(t1 - t0), '-vf', f'fps=1,scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
p = subprocess.Popen(cmd, stdout=subprocess.PIPE, bufsize=10**8); i = 0; scores = []
while True:
    raw = p.stdout.read(W * H * 3)
    if len(raw) < W * H * 3: break
    fr = np.frombuffer(raw, np.uint8).reshape(H, W, 3).astype(int); t = t0 + i; i += 1
    m = (np.abs(fr - col).sum(-1) < tol).astype(np.uint8)
    n, lab, st, ce = cv2.connectedComponentsWithStats(m, 8)
    if n > 1:
        j = 1 + st[1:, cv2.CC_STAT_AREA].argmax(); scores.append((int(st[j, cv2.CC_STAT_AREA]), t, fr.astype(np.uint8)))
p.stdout.close(); p.wait()
scores.sort(key=lambda s: -s[0]); top = scores[:30]
cell = (320, 176); cols = 6; rows = (len(top) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell[0], max(1, rows) * (cell[1] + 14)), (20, 20, 25)); d = ImageDraw.Draw(sheet)
for k, (a, t, fr) in enumerate(top):
    im = Image.fromarray(fr).resize(cell); x, y = (k % cols) * cell[0], (k // cols) * (cell[1] + 14)
    sheet.paste(im, (x, y)); d.text((x + 2, y + cell[1]), f'{t:.0f}s area {a}', fill=(230, 230, 230))
sheet.save(os.path.join(out, 'sheet.png')); print(files[0][:40], 'top', [(t, a) for a, t, _ in top[:8]])

"""strip.py "<episode substring>" t0 t1 out.png [fps=1] [cols=10] — a filmstrip contact sheet with time labels (192x106 cells)."""
import sys, os, subprocess
from PIL import Image, ImageDraw
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
sub, t0, t1, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4]
fps = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0; cols = int(sys.argv[6]) if len(sys.argv) > 6 else 10
files = [f for f in sorted(os.listdir(EP)) if sub in f]; assert files, sub
cw, ch = 192, 106
raw = subprocess.run(['ffmpeg', '-nostdin', '-v', 'error', '-ss', f'{t0:.3f}', '-i', os.path.join(EP, files[0]), '-t', f'{t1 - t0:.3f}', '-vf', f'fps={fps},scale={cw}:{ch}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'], capture_output=True).stdout
n = len(raw) // (cw * ch * 3); rows = (n + cols - 1) // cols
sheet = Image.new('RGB', (cols * (cw + 2), rows * (ch + 14)), (18, 18, 22)); d = ImageDraw.Draw(sheet)
for j in range(n):
    im = Image.frombytes('RGB', (cw, ch), raw[j * cw * ch * 3:(j + 1) * cw * ch * 3])
    x, y = (j % cols) * (cw + 2), (j // cols) * (ch + 14); sheet.paste(im, (x, y)); d.text((x + 2, y + ch), f'{t0 + j / fps:.2f}', fill=(235, 235, 235))
sheet.save(out); print(out, sheet.size, n, 'frames', files[0][:40])

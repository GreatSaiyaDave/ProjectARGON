"""gestwho.py <gest dir...> — guess the character of each scanner window from the hair band above the face box and the
torso band below it (dominant colours), against the DK anime palette. Appends 'who' to windows.json and prints a table."""
import sys, os, json, subprocess, numpy as np
EP = '/home/greatsaiyadave/Downloads/YouTube Downloader'
W, H = 640, 352
# (name, hair rgb, torso rgb or None) — DK-era dub frames (approximate lit colours)
PAL = [('yugi', (40, 30, 60), (30, 50, 140)), ('yugi', (190, 40, 110), (30, 50, 140)), ('kaiba', (120, 75, 40), (235, 235, 240)), ('kaiba', (120, 75, 40), (40, 60, 130)),
       ('joey', (230, 200, 90), (40, 110, 60)), ('joey', (230, 200, 90), (30, 50, 140)), ('mai', (240, 215, 120), (110, 60, 170)), ('tea', (120, 70, 40), (240, 150, 200)),
       ('tristan', (110, 70, 45), (60, 70, 100)), ('weevil', (60, 150, 140), (240, 240, 240)), ('rex', (170, 40, 40), (140, 80, 50)), ('mako', (30, 40, 110), (20, 80, 100)),
       ('keith', (220, 190, 100), (40, 40, 50)), ('panik', (25, 25, 35), (25, 25, 35)), ('pegasus', (220, 220, 230), (200, 40, 50)), ('bonz', (60, 70, 60), (90, 90, 100)),
       ('bakura', (240, 240, 245), (60, 130, 240)), ('mokuba', (30, 30, 40), (210, 190, 120)), ('paradox', (215, 180, 150), (200, 120, 60)), ('paradox', (215, 180, 150), (90, 180, 130)),
       ('grandpa', (170, 170, 170), (60, 110, 60))]
def dom(px, k=3):
    px = px.reshape(-1, 3).astype(np.float32)
    if len(px) < 20: return (0, 0, 0)
    import cv2
    _, lab, cen = cv2.kmeans(px, k, None, (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 10, 1.0), 2, cv2.KMEANS_PP_CENTERS)
    n = np.bincount(lab.ravel(), minlength=k); return tuple(int(v) for v in cen[n.argmax()])
for d in sys.argv[1:]:
    jp = os.path.join(d, 'windows.json')
    if not os.path.exists(jp): continue
    j = json.load(open(jp)); path = os.path.join(EP, j['ep'])
    for w in j['windows']:
        t = w['t0'] + 0.5
        raw = subprocess.run(['ffmpeg', '-nostdin', '-v', 'error', '-ss', f'{t:.3f}', '-i', path, '-frames:v', '1', '-vf', f'scale={W}:{H}', '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1'], capture_output=True).stdout
        if len(raw) < W * H * 3: continue
        fr = np.frombuffer(raw, np.uint8).reshape(H, W, 3)
        x, y, fw, fh = w['face']
        hair = fr[max(0, int(y - 0.55 * fh)):max(1, int(y + 0.15 * fh)), max(0, int(x - 0.15 * fw)):min(W, int(x + 1.15 * fw))]
        torso = fr[min(H - 2, int(y + 1.25 * fh)):min(H, int(y + 2.4 * fh)), max(0, int(x - 0.4 * fw)):min(W, int(x + 1.4 * fw))]
        hc, tc = dom(hair), dom(torso)
        best = min(PAL, key=lambda p: np.abs(np.array(hc) - p[1]).sum() + 0.6 * np.abs(np.array(tc) - p[2]).sum())
        w['who'] = best[0]; w['hair'] = hc; w['torso'] = tc
        print(f"{os.path.basename(d)} {w['file'][:3]} {w['t0']:7.1f}s face{fw:4d}px score {w['score']:.2f}  {best[0]:8s} hair{hc} torso{tc}")
    json.dump(j, open(jp, 'w'), indent=0)

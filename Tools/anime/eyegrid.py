"""eyegrid.py frame.png [frame2.png ...] -> <frame>_eyes.png: the detected face's eye band at 1:1 with a 20 px grid labelled in frame px"""
import sys, os, cv2, numpy as np
from PIL import Image, ImageDraw
casc = cv2.CascadeClassifier(os.path.join(os.path.dirname(__file__), 'lbpcascade_animeface.xml'))
for f in sys.argv[1:]:
    im = Image.open(f).convert('RGB'); a = cv2.cvtColor(np.array(im), cv2.COLOR_RGB2GRAY); a = cv2.equalizeHist(a)
    faces = sorted(casc.detectMultiScale(a, 1.1, 5, minSize=(100, 100)), key=lambda b: -b[2])
    if len(faces) == 0: x, y, w, h = 200, 60, 880, 600; print(f, 'no face: full grid')
    else: x, y, w, h = faces[0]
    x0, y0, x1, y1 = max(0, x - w // 6), max(0, y - h // 10), min(im.width, x + w + w // 6), min(im.height, y + int(h * 0.75))
    crop = im.crop((x0, y0, x1, y1)); d = ImageDraw.Draw(crop)
    for gx in range((x0 // 20 + 1) * 20, x1, 20):
        d.line([(gx - x0, 0), (gx - x0, crop.height)], fill=(255, 0, 0) if gx % 100 == 0 else (255, 150, 150), width=1)
        if gx % 50 == 0: d.text((gx - x0 + 2, 2), str(gx), fill=(255, 255, 0)); d.text((gx - x0 + 2, crop.height - 12), str(gx), fill=(255, 255, 0))
    for gy in range((y0 // 20 + 1) * 20, y1, 20):
        d.line([(0, gy - y0), (crop.width, gy - y0)], fill=(255, 0, 0) if gy % 100 == 0 else (255, 150, 150), width=1)
        if gy % 50 == 0: d.text((2, gy - y0 + 2), str(gy), fill=(255, 255, 0)); d.text((crop.width - 30, gy - y0 + 2), str(gy), fill=(255, 255, 0))
    out = f.replace('.png', '_eyes.png'); crop.save(out); print(out, 'box', (x, y, w, h), 'crop', (x0, y0, x1, y1))

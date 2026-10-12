#!/usr/bin/env python3
"""grab.py <dub S1 episode number> <seconds> <out.png> : one full-res frame (1280x720) cut with ffmpeg -ss <t> -frames:v 1
from the owner's library (~/Downloads/YouTube Downloader/*Season 1 Episode <n>.mp4). Writes only <out.png>."""
import sys, os, subprocess, glob
EP = os.path.expanduser('~/Downloads/YouTube Downloader')
def path(ep):
    c = [f for f in glob.glob(os.path.join(EP, f'*Yu-Gi-Oh Season 1 Episode {ep}.mp4'))]
    assert len(c) == 1, (ep, c); return c[0]
if __name__ == '__main__':
    ep, t, out = sys.argv[1], sys.argv[2], sys.argv[3]
    subprocess.run(['ffmpeg', '-nostdin', '-v', 'error', '-y', '-ss', str(t), '-i', path(ep), '-frames:v', '1', out], check=True)

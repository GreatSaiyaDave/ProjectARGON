#!/usr/bin/env python3
"""
Fetch DeviantArt character renders (2D transparent) for the heroes and Bakura.
"""
import os, re, sys, time, requests, html
from urllib.parse import quote

UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
OUT = "/home/greatsaiyadave/argon-avatar/research/anime_variants/da_renders"
os.makedirs(OUT, exist_ok=True)

chars = [
    ("yami_yugi", "yami+yugi+render"),
    ("joey", "jonouchi+render"),
    ("tea", "anzu+mazaki+render"),
    ("tristan", "tristan+taylor+render"),
    ("bakura", "ryou+bakura+render"),
    ("yami_bakura", "yami+bakura+render"),
]

session = requests.Session()
session.headers.update({"User-Agent": UA})

def search(q):
    url = f"https://www.deviantart.com/search?q={quote(q)}"
    r = session.get(url, timeout=30)
    r.raise_for_status()
    return r.text

def best_image_from_page(page_html):
    candidates = re.findall(r'https://images-wixmp[^"\'\s]+', page_html)
    if not candidates:
        return None
    best = None
    bestw = -1
    for c in candidates:
        m = re.search(r'/[a-z]+/w_([0-9]+),', c)
        if m:
            w = int(m.group(1))
            if w > bestw:
                bestw = w; best = c
            continue
    if bestw >= 600 and best and 'token=' in best:
        return best
    for c in candidates:
        if 'token=' in c and ('fullview' in c or 'fit' in c or 'fill' in c):
            return c
    return None

def fetch_img(url, outpath):
    r = session.get(url, timeout=60)
    r.raise_for_status()
    with open(outpath,'wb') as f: f.write(r.content)
    return True

def get_deviation_links(html):
    links = re.findall(r'href="https://www\.deviantart\.com/[a-zA-Z0-9_-]+/art/[^"]+"', html)
    out = []
    seen = set()
    for l in links:
        href = l[6:-1]
        if href.endswith('#comments'): continue
        if href in seen: continue
        seen.add(href)
        out.append(href)
    return out

def main():
    for cid, q in chars:
        print(f"=== {cid}: {q} ===")
        try:
            sh = search(q)
        except Exception as e:
            print("search err",e); continue
        links = get_deviation_links(sh)[:6]
        if not links:
            print("no links"); continue
        got = 0
        for i, link in enumerate(links):
            try:
                r = session.get(link, timeout=30); r.raise_for_status()
            except Exception as e:
                print("page err",e); continue
            img = best_image_from_page(r.text)
            if not img:
                print("no img for", link); continue
            outp = os.path.join(OUT, f"{cid}_{i:02d}.png")
            try:
                fetch_img(img, outp)
                print("saved", outp)
                got+=1
            except Exception as e:
                print("fetch err",e)
            time.sleep(0.8)
        print("got", got)

if __name__=="__main__":
    main()

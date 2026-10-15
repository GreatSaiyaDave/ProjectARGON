#!/usr/bin/env python3
"""
Fetch a full DeviantArt gallery of GravekeepersBrewer's "Yu-Gi-Oh character renders"
series. Each gallery is one era (Duelist Kingdom, Battle City, ...) and its items are
front-facing, full-body character renders composited on a pure-black background at
~1000x1500.

The web gallery page only exposes the first 11 items; the RSS mirror
(backend.deviantart.com/rss.xml?q=gallery:<artist>/<id>&type=deviation) returns the whole
gallery. For each item we open the deviation and download the full-resolution ORIGINAL
(the bare /f/<folder>/<file>.png?token= URL, no /v1/ transform), then record objective
cutout metrics.

Usage:  python3 fetch_da_gallery.py <gallery-id> <era-key> [artist]
Output: anime_variants/da_gallery/<era-key>/<slug>.png  +  <era-key>.json
"""
import os, re, sys, json, time, html
import requests
from PIL import Image
import numpy as np

UA = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
BASE = "/home/greatsaiyadave/argon-avatar/research/anime_variants/da_gallery"

session = requests.Session()
session.headers.update({"User-Agent": UA})


def rss_items(gallery_id, artist):
    url = f"https://backend.deviantart.com/rss.xml?q=gallery%3A{artist}%2F{gallery_id}&type=deviation"
    r = session.get(url, timeout=40)
    r.raise_for_status()
    out = []
    for it in re.findall(r"<item>(.*?)</item>", r.text, re.S):
        t = re.search(r"<title>(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?</title>", it)
        l = re.search(r"<link>(.*?)</link>", it)
        d = re.search(r'<media:description type="html">(.*?)</media:description>', it, re.S)
        if not (t and l):
            continue
        desc = html.unescape(html.unescape(d.group(1))) if d else ""
        desc = re.sub(r"<[^>]+>", " ", desc).strip()
        out.append({"title": html.unescape(t.group(1)).strip(), "link": l.group(1).strip(), "desc": desc})
    return out


def image_url(page_html):
    urls = re.findall(r'https://images-wixmp[^"\'\s\\]+', page_html)
    urls = [u.replace('\\u002F', '/').replace('\\/', '/') for u in urls]
    if not urls:
        return None
    base = [u for u in urls if '/v1/' not in u and 'token=' in u]
    return base[0] if base else None


def slugify(s, i):
    s = re.sub(r'[^A-Za-z0-9]+', '_', s).strip('_').lower()[:56]
    return f"{s}" if i == 0 else f"{s}_{i}"


def metrics(path):
    im = Image.open(path)
    a = np.array(im.convert("RGB"))
    H, W, _ = a.shape
    dark = a.sum(axis=2) <= 28
    corners = [a[0, 0].sum(), a[0, W - 1].sum(), a[H - 1, 0].sum(), a[H - 1, W - 1].sum()]
    return {"size": [W, H], "black_bg": bool(max(corners) <= 30), "dark_frac": round(float(dark.mean()), 3)}


def main():
    gid, key = sys.argv[1], sys.argv[2]
    artist = sys.argv[3] if len(sys.argv) > 3 else "gravekeepersbrewer"
    outdir = os.path.join(BASE, key)
    os.makedirs(outdir, exist_ok=True)

    items = rss_items(gid, artist)
    print(f"{key}: {len(items)} items from RSS")

    seen = {}
    manifest = {"gallery": gid, "era": key, "artist": artist, "items": {}}
    for it in items:
        n = seen.get(it["title"], 0)
        seen[it["title"]] = n + 1
        slug = slugify(it["title"], n)
        path = os.path.join(outdir, slug + ".png")
        rec = dict(it); rec["slug"] = slug
        try:
            pr = session.get(it["link"], timeout=40)
            pr.raise_for_status()
            img = image_url(pr.text)
            if not img:
                rec["note"] = "no base image url"
            else:
                ir = session.get(img, timeout=90)
                ir.raise_for_status()
                with open(path, "wb") as f:
                    f.write(ir.content)
                rec["image_url"] = img
                rec.update(metrics(path))
                print(f"  {it['title'][:34]:34} {rec['size']} black_bg={rec['black_bg']}")
        except Exception as e:
            rec["note"] = f"err {e}"
            print(f"  {it['title'][:34]:34} FAILED {e}")
        manifest["items"][slug] = rec
        time.sleep(0.4)

    with open(os.path.join(BASE, key + ".json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print("wrote", os.path.join(BASE, key + ".json"))


if __name__ == "__main__":
    main()

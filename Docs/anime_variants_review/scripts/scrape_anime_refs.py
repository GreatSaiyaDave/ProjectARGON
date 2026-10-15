#!/usr/bin/env python3
"""Scrape anime screenshots per character from the wiki (for same-domain references).
Filters out video-game art by keyword so references stay in the anime domain."""
import json, os, re, sys, time, urllib.parse, urllib.request

OUT = "/tmp/opencode/anime_refs"
UA = {"User-Agent": "argon-anime-research/1.0 (local)"}
PAGES = {
    "yami_yugi": "Yugi Muto", "kaiba": "Seto Kaiba", "joey": "Joey Wheeler",
    "mai": "Mai Valentine", "tea": "Téa Gardner", "weevil": "Weevil Underwood",
    "rex": "Rex Raptor", "mako": "Mako Tsunami", "marik": "Marik Ishtar",
    "bakura": "Ryo Bakura", "bandit_keith": "Bandit Keith", "ishizu": "Ishizu Ishtar",
    "odion": "Odion", "pegasus": "Maximillion Pegasus", "mokuba": "Mokuba Kaiba",
    "tristan": "Tristan Taylor",
}
NAMES = {
    "yami_yugi": ["yugi", "yami", "muto", "mutou", "atem"],
    "kaiba": ["kaiba"], "joey": ["joey", "jonouchi"], "mai": ["mai", "kujaku"],
    "tea": ["tea", "téa", "anzu"], "weevil": ["weevil", "haga"],
    "rex": ["rex", "ryuzaki"], "mako": ["mako", "kajiki"],
    "marik": ["marik", "malik"], "bakura": ["bakura", "ryou", "ryo"],
    "bandit_keith": ["keith"], "ishizu": ["ishizu", "isis"],
    "odion": ["odion", "rishid"], "pegasus": ["pegasus", "crawford"],
    "mokuba": ["mokuba"], "tristan": ["tristan", "honda"],
}
# video-game / non-anime markers
BLOCK = ["duli", "lod", "dod", "dds", "dm1", "dm2", "dm4", "mddg", "tfsp", "wc0",
         "wc4", "tsc", "sdd", "fmr", "eds", "ddm", "cmc", "scb", "ntr", "dsod",
         "accelerator", "nightmare", "concept", "linework", "lineart", " linework",
         "manga", "portal", "capsule", "pyramid", "bam", "fsa", "duelistkingdom",
         "-dt.", " md.", "online", "debut", "decks", "gospel", "featuring", "icon",
         "broom", "flyer", "card ", "deck", "toon", "screenshot", ".gif"]


def api(**kw):
    kw.setdefault("format", "json"); kw.setdefault("formatversion", "2")
    u = "https://yugipedia.com/api.php?" + urllib.parse.urlencode(kw)
    return json.loads(urllib.request.urlopen(
        urllib.request.Request(u, headers=UA), timeout=45).read().decode("utf-8", "replace"))


def anime_like(t, cid):
    low = t.lower()
    if any(b in low for b in BLOCK):
        return False
    if any(n in low for n in NAMES[cid]):
        return True
    return bool(re.search(r"dmx\d+|yugioh\s*\d+|yu-gi-oh\s*\d+", low))


def main():
    os.makedirs(OUT, exist_ok=True)
    summary = {}
    for cid, page in PAGES.items():
        try:
            j = api(action="query", prop="images", imlimit="500",
                    titles=page, redirects="1")
            pg = j["query"]["pages"][0]
            imgs = [x["title"] for x in pg.get("images", [])]
        except Exception as e:
            print(cid, "list fail", e); continue
        sel = [t for t in imgs if anime_like(t, cid)]
        urls = {}
        for i in range(0, len(sel), 50):
            try:
                jj = api(action="query", prop="imageinfo", iiprop="url",
                         titles="|".join(sel[i:i+50]), redirects="1")
                for p in jj["query"]["pages"]:
                    ii = p.get("imageinfo")
                    if ii:
                        urls[p["title"]] = ii[0]["url"]
            except Exception as e:
                print(cid, "info fail", e)
        d = os.path.join(OUT, cid); os.makedirs(d, exist_ok=True)
        n = 0
        for t, u in urls.items():
            ext = os.path.splitext(u)[1].lower() or ".png"
            if ext not in (".png", ".jpg", ".jpeg"):
                continue
            try:
                data = urllib.request.urlopen(
                    urllib.request.Request(u, headers=UA), timeout=60).read()
                open(os.path.join(d, f"{n:02d}{ext}"), "wb").write(data)
                n += 1
            except Exception as e:
                print("  dl fail", t, e)
            if n >= 14:
                break
        summary[cid] = n
        print(f"{cid:13} {n} anime refs   ({len(sel)} candidates)")
        time.sleep(0.1)
    json.dump(summary, open(os.path.join(OUT, "summary.json"), "w"), indent=1)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
Scrape per-character appearance/outfit descriptions from Yugipedia, focusing on the
Duelist Kingdom (season 1) look.  Saves outfit_meta.json with the raw 'Design'/'Appearance'
section plus any 'Duelist Kingdom' / 'season 1' prose we can find.
"""
import json, os, re, time, urllib.parse, urllib.request

OUT = "/home/greatsaiyadave/argon-avatar/research/anime_variants/outfit_meta.json"
UA = {"User-Agent": "argon-anime-research/1.0 (local)"}
PAGES = {
    "yami_yugi": ["Yugi Muto", "Yami Yugi", "Atem"],
    "kaiba": ["Seto Kaiba"], "joey": ["Joey Wheeler"], "mai": ["Mai Valentine"],
    "tea": ["Téa Gardner"], "weevil": ["Weevil Underwood"], "rex": ["Rex Raptor"],
    "mako": ["Mako Tsunami"], "marik": ["Marik Ishtar"], "bakura": ["Ryo Bakura"],
    "bandit_keith": ["Bandit Keith"], "ishizu": ["Ishizu Ishtar"], "odion": ["Odion"],
    "pegasus": ["Maximillion Pegasus"], "mokuba": ["Mokuba Kaiba"],
    "tristan": ["Tristan Taylor"],
}
WANT = re.compile(r"design|appearance|outfit|clothing|attire", re.I)


def wikitext(title):
    u = ("https://yugipedia.com/api.php?action=parse&page=" +
         urllib.parse.quote(title) + "&prop=wikitext&format=json&formatversion=2&redirects=1")
    j = json.loads(urllib.request.urlopen(
        urllib.request.Request(u, headers=UA), timeout=45).read().decode("utf-8", "replace"))
    return j.get("parse", {}).get("wikitext", "") or ""


def sections(text):
    """Split a wikitext page into {heading: body} for level-2/3 headings."""
    out, cur, buf = {}, "lead", []
    for line in text.splitlines():
        m = re.match(r"^(={2,4})\s*(.+?)\s*\1\s*$", line)
        if m:
            out[cur] = "\n".join(buf).strip()
            cur, buf = m.group(2), []
        else:
            buf.append(line)
    out[cur] = "\n".join(buf).strip()
    return out


def clean(t, n=2500):
    t = re.sub(r"\{\{[^{}]*\}\}", " ", t)
    t = re.sub(r"\{\{[^{}]*\}\}", " ", t)
    t = re.sub(r"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]", r"\1", t)
    t = re.sub(r"<ref[^>]*>.*?</ref>", " ", t, flags=re.S)
    t = re.sub(r"<[^>]+>", " ", t)
    t = re.sub(r"\[https?://\S+ \S+\]", " ", t)
    t = re.sub(r"'{2,}", "", t)
    t = re.sub(r"\s+", " ", t)
    return t.strip()[:n]


res = {}
for cid, titles in PAGES.items():
    got = {}
    for t in titles:
        try:
            wt = wikitext(t)
        except Exception as e:
            print(cid, t, "ERR", e); continue
        if not wt:
            continue
        secs = sections(wt)
        d = {k: v for k, v in secs.items() if WANT.search(k)}
        # pull "Duelist Kingdom" paragraphs from anywhere
        dk = [clean(p, 600) for p in re.split(r"\n\s*\n", wt)
              if re.search(r"duelist kingdom|season 1|first season", p, re.I)]
        got[t] = {"sections": {k: clean(v) for k, v in d.items()},
                  "duelist_kingdom": dk[:6]}
        time.sleep(0.15)
    res[cid] = got

json.dump(res, open(OUT, "w"), indent=1)
print("wrote", OUT)
for cid, g in res.items():
    sec_keys = [k for t in g for k in g[t]["sections"]]
    dk = sum(len(g[t]["duelist_kingdom"]) for t in g)
    print(f"  {cid:13} pages={len(g)} sections={sec_keys[:4]} dk_paras={dk}")

#!/usr/bin/env python3
"""
Scrape per-episode cast metadata for DM Season 1 from Yugipedia.

Two sources, unioned per episode:
  1. the ==Cast== table (characters with credited roles), when present;
  2. character wikilinks in the infobox / episode-summary / Summary / Featured-Duel
     prose (catches non-speaking appearances too).

Output: episode_cast.json (slug -> ordered list of our character ids),
        episode_cast_raw.json (slug -> ordered raw names).

stdlib only.
"""
import json, re, sys, time, urllib.parse, urllib.request, os

HERE = os.path.dirname(os.path.abspath(__file__))
UA = {"User-Agent": "argon-anime-research/1.0 (local)"}

ALIASES = {
    "yami_yugi":    ["yugi muto", "yami yugi", "dark yugi", "pharaoh", "atem", "yugi"],
    "kaiba":        ["seto kaiba", "kaiba"],
    "joey":         ["joey wheeler", "katsuya jonouchi", "jonouchi", "joey"],
    "mai":          ["mai valentine", "mai kujaku", "mai"],
    "tea":          ["tea gardner", "téa gardner", "anzu mazaki", "anzu", "tea", "téa"],
    "weevil":       ["weevil underwood", "insector haga", "haga", "weevil"],
    "rex":          ["rex raptor", "dinosaur ryuzaki", "ryuzaki", "rex"],
    "mako":         ["mako tsunami", "ryota kajiki", "kajiki", "mako"],
    "marik":        ["marik ishtar", "marik"],
    "bakura":       ["ryo bakura", "ryou bakura", "yami bakura", "bakura"],
    "bandit_keith": ["bandit keith", "keith howard", "keith"],
    "ishizu":       ["ishizu ishtar", "ishizu"],
    "odion":        ["odion", "rishid", "rashid"],
    "pegasus":      ["maximillion pegasus", "pegasus j. crawford", "pegasus"],
    "mokuba":       ["mokuba kaiba", "mokuba"],
    "tristan":      ["tristan taylor", "hiroto honda", "honda", "tristan"],
}
# longest alias first so "mokuba kaiba" beats "kaiba", "seto kaiba" beats "kaiba"
ALIST = sorted(((a, c) for c, als in ALIASES.items() for a in als),
               key=lambda x: -len(x[0]))

TRAILING = ["==Differences in adaptations==", "==In other languages==", "==Mistakes==",
            "==Errors==", "==Featured cards==", "==Featured dice==", "==References==",
            "==Notes==", "==Quotes=="]


def http_json(title):
    u = ("https://yugipedia.com/api.php?action=parse&page="
         + urllib.parse.quote(title) + "&prop=wikitext&format=json&formatversion=2&redirects=1")
    req = urllib.request.Request(u, headers=UA)
    with urllib.request.urlopen(req, timeout=40) as r:
        return json.loads(r.read().decode("utf-8", "replace"))


def match_alias(name):
    low = re.sub(r"<[^>]+>", "", name).lower().strip()
    for a, cid in ALIST:
        if re.search(r"(?<![a-z])" + re.escape(a) + r"(?![a-z])", low):
            return cid
    return None


def cast_table_names(section):
    """First cell of every data row in the ==Cast== wikitable."""
    out = []
    for chunk in re.split(r"\n\|-", section):
        cells = []
        for line in chunk.splitlines():
            s = line.strip()
            if s.startswith("|") and not s.startswith(("|-", "|}")):
                cells.append(s[1:])
        if not cells:
            continue
        cell = cells[0]
        # drop cell attributes like  style="..." | value
        if "|" in cell and "[[" not in cell.split("|")[0]:
            cell = cell.split("|")[-1]
        links = re.findall(r"\[\[([^\]\|]+)(?:\|([^\]]*))?\]\]", cell)
        if links:
            out.append((links[0][1] or links[0][0]).strip())
        else:
            out.append(cell.strip())  # e.g. "Other", "Narration"
    return out


def summary_names(text):
    out = []
    for tgt, disp in re.findall(r"\[\[([^\]\|]+)(?:\|([^\]]*))?\]\]", text):
        out.append((disp or tgt).strip())
    return out


def main():
    cast, seq, raw = {}, {}, {}
    strong_cast = {}
    title_by_slug = {}
    for n in range(1, 50):
        slug = f"dm_s1e{n:03d}"
        title = f"Yu-Gi-Oh! - Episode {n:03d}"
        try:
            wt = http_json(title)["parse"]["wikitext"]
        except Exception as e:
            print(f"{slug}: FETCH FAIL {e}", file=sys.stderr)
            cast[slug] = []
            continue

        tm = re.search(r"\|\s*en_name\s*=\s*(.+)", wt) or re.search(r"\|\s*english\s*=\s*(.+)", wt)
        title_en = tm.group(1).strip() if tm else ""

        m = re.search(r"==\s*Cast\s*==(.*?)(?=\n==[^=\n]|\Z)", wt, re.S)
        tnames = cast_table_names(m.group(1)) if m else []

        cut = wt
        pos = [cut.find(t) for t in TRAILING if cut.find(t) >= 0]
        if pos:
            cut = cut[:min(pos)]
        names = tnames + summary_names(cut)

        def ids_of(nms):
            out = []
            for nm in nms:
                cid = match_alias(nm)
                if cid and cid not in out:
                    out.append(cid)
            return out
        strong = ids_of(tnames)
        ids = ids_of(names)
        cast[slug] = ids
        strong_cast[slug] = strong
        seq[slug] = names
        title_by_slug[slug] = title_en
        print(f"{slug}: {ids}   (names {len(names)})")
        time.sleep(0.15)

    json.dump(cast, open(os.path.join(HERE, "episode_cast.json"), "w"), indent=1)
    json.dump(seq, open(os.path.join(HERE, "episode_cast_raw.json"), "w"),
              indent=1, ensure_ascii=False)
    json.dump({k: {"title": title_by_slug.get(k, ""), "cast": cast[k],
                   "cast_strong": strong_cast.get(k, [])} for k in cast},
              open(os.path.join(HERE, "episode_meta.json"), "w"), indent=1)
    print("\nwrote episode_cast.json for", len(cast), "episodes;",
          sum(1 for v in cast.values() if not v), "empty")


if __name__ == "__main__":
    main()

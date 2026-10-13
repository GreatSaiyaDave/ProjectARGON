#!/usr/bin/env python3
"""audit.py — the canon-texture audit (Phase 1).

Compares the hair colours the game *actually shows* for each canon duelist against the anime-frame
measurements in measured/hair_summary.json, and inventories the face decals + garment albedos.

It reads (never writes) the C# look tables and the avatar resources, and writes ONLY under Tools/anime/out/:

    Tools/anime/animepy audit.py                # -> out/canon_texture_audit.md + out/canon_texture_audit.json

Resolution model (mirrors Assets/Scripts/WRLDZ/Data/*.cs):
  * AiDuelistLooks.For(name) builds the base look (hair style + optional .WithHair hex) and calls
    CharacterLooks.Match(name, era).ApplyTo(), which overwrites hairPattern/hairHex when the matched row sets them.
  * CharacterLook.ApplyTo always sets hairPattern, but sets hairHex only when the row's HairHex is non-empty
    (so a row with HairHex == null keeps the AiDuelistLooks .WithHair value).
  * HairPattern.Original (0) ignores hairHex entirely and draws the hairstyle's own palette from
    Hair/hair_catalog.json. Solid/Tips/TriColor/Streaks/Split use the recolour zones
    (AvatarAppearance.PatternUses).

Owner rule (2026-10-11): "Do not make stuff up. Precision and accuracy are vital." Every number here is
either a parsed code value or a measured frame value with its citation in hair_summary.json.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DATA = os.path.join(ROOT, "Assets", "Scripts", "WRLDZ", "Data")
RES = os.path.join(ROOT, "Assets", "Resources", "WRLDZ", "Avatar3D")
OUT = os.path.join(HERE, "out")

# The measured character key (hair_summary.json) + which measured zone fills which hair role.
# "lit" is the daylight flat fill; the shade is reported alongside but not used as the lit target.
MEASURED = {
    "yugi_bc":      ("Yugi",      {"root": "core", "tip": "tips", "fringe": "bangs_lit"}),
    "yami_yugi":    ("Yami Yugi", {"root": "core", "tip": "tips", "fringe": "bangs_lit"}),
    "yugi_dk":      ("Yugi",      {"root": "core", "tip": "tips", "fringe": "bangs_lit"}),
    "yami_yugi_dk": ("Yami Yugi", {"root": "core", "tip": "tips", "fringe": "bangs_lit"}),
    "kaiba_bc":     ("Kaiba",  {"root": "lit"}),
    "kaiba_dk":     ("Kaiba",  {"root": "lit"}),
    "joey_bc":      ("Joey",   {"root": "lit"}),
    "tea_bc":       ("Tea",    {"root": "lit"}),
    "bakura_bc":    ("Bakura", {"root": "lit"}),
    "yami_bakura":  ("Yami Bakura (castle e40)", {"root": "lit"}),
    "mokuba_bc":    ("Mokuba", {"root": "lit", "streak": "highlight"}),
    "weevil_bc":    ("Weevil", {"root": "lit"}),
    "rex_bc":       ("Rex",    {"root": "brown_lit", "fringe": "fringe"}),
    "mako_bc":      ("Mako",   {"root": "base", "streak": "highlight"}),
    "mako_dk":      ("Mako",   {"root": "base"}),
    "mai_bc":       ("Mai",    {"root": "lit"}),
    "keith_dk":     ("Keith",  {"root": "lit"}),
    "bonz_dk":      ("Bonz",   {"root": "lit"}),
    "pegasus_dk":   ("Pegasus", {"root": "lit"}),
    "panik_dk":     ("PaniK (night only)", {"root": "lit"}),
    "tristan_bc":   ("Tristan", {"root": "lit"}),
}
# Canon duelists with no anime-frame measurement yet (the plan's Phase 1 gap list).
UNMEASURED = {
    "ishizu_bc": "Mai's hair mesh; colour from Duel Links sn0013 art, no frame measured",
    "odion_bc": "Kaiba's hair mesh; colour from Duel Links sn0014 art, no frame measured",
    "duke_bc": "Joey's hair mesh; colour from Duel Links sn0034 art, no frame measured",
    "marik_bc": "own marik_ishtar style; skin #A8704A from DL art; hair is the style's own palette",
    "para": "bald (hairStyleId 'none'); no hair to measure",
    "dox": "bald (hairStyleId 'none'); no hair to measure",
}

PATTERN_USES = {
    "root":   {"Solid", "Tips", "TriColor", "Streaks", "Split"},
    "tip":    {"Tips", "TriColor"},
    "fringe": {"TriColor"},
    "streak": {"Streaks"},
    "side":   {"Split"},
}
PATTERN_NUM = {0: "Original", 1: "Solid", 2: "Tips", 3: "TriColor", 4: "Streaks", 5: "Split"}

# The effective face decal per canon look: CharacterLook.FaceId wins, else AiDuelistLooks.FaceFor(name)
# (replicated exactly: yami yugi > mokuba > kaiba > yugi ... tea special-cased in Match; non-canon -> "").
FACE_BY_LOOK = {
    "yugi_bc": "yugi_muto", "yami_yugi": "yami_yugi", "yugi_dk": "yugi_muto", "yami_yugi_dk": "yami_yugi",
    "kaiba_bc": "seto_kaiba", "kaiba_dk": "seto_kaiba",
    "joey_bc": "joey_wheeler", "tea_bc": "tea",
    "mai_bc": "mai_valentine", "bakura_bc": "bakura", "yami_bakura": "yami_bakura",
    "marik_bc": "marik", "mokuba_bc": "mokuba_kaiba", "weevil_bc": "weevil",
    "rex_bc": "rex_raptor", "mako_bc": "mako", "mako_dk": "mako",
    "keith_dk": "bandit_keith", "bonz_dk": "bonz", "pegasus_dk": "pegasus",
    "panik_dk": "panik", "ishizu_bc": "ishizu", "odion_bc": "odion",
    "duke_bc": "duke", "tristan_bc": "tristan", "para": "para", "dox": "dox",
}


def read(path):
    with open(path, encoding="utf-8") as fh:
        return fh.read()


def strip_line_comments(src):
    out = []
    for line in src.splitlines():
        i = 0
        res = []
        in_str = False
        esc = False
        while i < len(line):
            c = line[i]
            if in_str:
                res.append(c)
                if esc:
                    esc = False
                elif c == "\\":
                    esc = True
                elif c == '"':
                    in_str = False
            else:
                if c == '"':
                    in_str = True
                    res.append(c)
                elif c == "/" and i + 1 < len(line) and line[i + 1] == "/":
                    break
                else:
                    res.append(c)
            i += 1
        out.append("".join(res))
    return "\n".join(out)


def split_args(s):
    """Split a C# argument list on top-level commas (quotes and parens aware)."""
    args, depth, in_str, esc, cur = [], 0, False, False, []
    for c in s:
        if in_str:
            cur.append(c)
            if esc:
                esc = False
            elif c == "\\":
                esc = True
            elif c == '"':
                in_str = False
            continue
        if c == '"':
            in_str = True
            cur.append(c)
        elif c in "([{":
            depth += 1
            cur.append(c)
        elif c in ")]}":
            depth -= 1
            cur.append(c)
        elif c == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
    if "".join(cur).strip():
        args.append("".join(cur).strip())
    return args


def find_calls(text, fname):
    """Every <fname>(...) call in text -> list of raw arg strings."""
    calls = []
    for m in re.finditer(r"\b" + re.escape(fname) + r"\s*\(", text):
        i = m.end()
        depth = 1
        in_str = False
        esc = False
        buf = []
        while i < len(text) and depth > 0:
            c = text[i]
            if in_str:
                buf.append(c)
                if esc:
                    esc = False
                elif c == "\\":
                    esc = True
                elif c == '"':
                    in_str = False
            else:
                if c == '"':
                    in_str = True
                    buf.append(c)
                elif c == "(":
                    depth += 1
                    buf.append(c)
                elif c == ")":
                    depth -= 1
                    if depth == 0:
                        break
                    buf.append(c)
                else:
                    buf.append(c)
            i += 1
        calls.append(split_args("".join(buf)))
    return calls


def lit(s):
    s = (s or "").strip()
    if s == "null":
        return None
    m = re.match(r'^"(.*)"$', s)
    return m.group(1) if m else s


def hexstr(s):
    v = lit(s)
    return v if v and v.startswith("#") else None


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def delta(hex_a, hex_b):
    if not hex_a or not hex_b:
        return None
    a, b = hex_rgb(hex_a), hex_rgb(hex_b)
    d = [b[i] - a[i] for i in range(3)]
    return {"dr": d[0], "dg": d[1], "db": d[2], "max": max(abs(x) for x in d)}


def verdict(d):
    if d is None:
        return "no-measurement"
    if d["max"] == 0:
        return "exact"
    if d["max"] <= 6:
        return "match"
    if d["max"] <= 20:
        return "close"
    return "GAP"


# ---------------------------------------------------------------- parse tables


def parse_character_looks():
    src = strip_line_comments(read(os.path.join(DATA, "CharacterLooks.cs")))
    rows = []
    for args in find_calls(src, "L"):
        if len(args) < 12:
            continue
        if (args[0].strip() or "").startswith("string"):
            continue  # the method signature `L(string id, ...)`, not a data row
        row = {
            "id": lit(args[0]),
            "name": lit(args[1]),
            "match": lit(args[2]),
            "body": lit(args[3]),
            "hairId": lit(args[4]),
            "faceId": lit(args[5]) or None,
            "shirtId": lit(args[8]),
            "bottomsId": lit(args[9]),
            "shoesId": lit(args[10]),
            "handsId": lit(args[11]),
            "hairPattern": 0,
            "hairHex": None,
            "hairTip": None,
            "hairFringe": None,
            "skinHex": None,
            "hatId": None,
        }
        for a in args[12:]:
            if ":" not in a:
                continue
            k, v = a.split(":", 1)
            k, v = k.strip(), v.strip()
            if k == "hairPattern":
                row["hairPattern"] = int(v)
            elif k == "hairHex":
                row["hairHex"] = hexstr(v)
            elif k == "hairTip":
                row["hairTip"] = hexstr(v)
            elif k == "hairFringe":
                row["hairFringe"] = hexstr(v)
            elif k == "skin":
                row["skinHex"] = hexstr(v)
            elif k == "hat":
                row["hatId"] = lit(v)
        rows.append(row)
    return rows


def parse_ai_duelist_looks():
    src = strip_line_comments(read(os.path.join(DATA, "AiDuelistLooks.cs")))
    rows = []
    for line in src.splitlines():
        if "Look(" not in line:
            continue
        calls = find_calls(line, "Look")
        if not calls:
            continue
        args = calls[0]
        if len(args) < 5:
            continue
        try:
            pattern = int(lit(args[4]))
        except (TypeError, ValueError):
            continue  # the method definition (`Look(int body, ...)`), not a call
        cond = line.split("Look(", 1)[0]
        m = re.search(r'Contains\("([^"]+)"\)', cond)
        if not m:
            m = re.search(r'==\s*"([^"]+)"', cond)
        withhair = re.search(r'\.WithHair\("([^"]+)"\)', line)
        row = {
            "token": (m.group(1) if m else cond.strip()),
            "hairId": lit(args[1]),
            "pattern": pattern,
            "withHair": withhair.group(1) if withhair else None,
        }
        rows.append(row)
    return rows


def style_palettes():
    cat = json.load(open(os.path.join(RES, "Hair", "hair_catalog.json"), encoding="utf-8"))
    return {s["id"]: s.get("palette") or {} for s in cat.get("styles", [])}


# ---------------------------------------------------------------- resolution


def resolve_hair(row, palettes, ai_rows):
    """The zones actually shown, + where the value came from."""
    style = palettes.get(row["hairId"] or "", {})
    ai = next((a for a in ai_rows if a["token"] and a["token"] in (row["match"] or "")), None)
    pat = PATTERN_NUM.get(row["hairPattern"], "Original")
    hexes = {z: None for z in ("root", "tip", "fringe", "streak", "side")}
    source = {}
    if row["hairPattern"] == 0:
        # Original: the style draws its own catalogue palette; hairHex is ignored.
        for z in hexes:
            hexes[z] = (style.get(z) or "").upper() or None
        source = {z: "hair_catalog style palette" for z in hexes}
    else:
        root = row["hairHex"] or (ai["withHair"] if ai else None)
        hexes["root"] = root
        source["root"] = "CharacterLooks row" if row["hairHex"] else ("AiDuelistLooks .WithHair" if root else "-")
        for z, rk in (("tip", "hairTip"), ("fringe", "hairFringe")):
            v = row[rk] or root
            uses = z in ({"tip"} if pat in ("Tips", "TriColor") else set()) or \
                   (z == "fringe" and pat == "TriColor")
            hexes[z] = v if uses else None
            source[z] = "CharacterLooks row" if row[rk] else ("= root" if v else "-")
    return {"pattern": pat, "hexes": hexes, "source": source, "style": style, "ai": ai}


# ---------------------------------------------------------------- main


def main():
    rows = parse_character_looks()
    ai_rows = parse_ai_duelist_looks()
    palettes = style_palettes()
    meas = json.load(open(os.path.join(HERE, "measured", "hair_summary.json"), encoding="utf-8"))["summary"]

    faces_dir = os.path.join(RES, "Faces")
    face_files = {f[:-4] for f in os.listdir(faces_dir) if f.endswith(".png")} if os.path.isdir(faces_dir) else set()

    report = {"generated_by": "Tools/anime/audit.py", "sources": {
        "looks": "Assets/Scripts/WRLDZ/Data/CharacterLooks.cs",
        "ai": "Assets/Scripts/WRLDZ/Data/AiDuelistLooks.cs",
        "measured": "Tools/anime/measured/hair_summary.json",
        "method": "Tools/anime/MEASUREMENT_NOTES.md",
    }, "hair": [], "unmeasured": [], "faces": [], "clothes": {}}

    # --- hair table
    for row in rows:
        if row["id"] not in MEASURED and row["id"] not in UNMEASURED:
            continue
        r = resolve_hair(row, palettes, ai_rows)
        entry = {"look": row["id"], "name": row["name"], "match": row["match"],
                 "hairStyle": row["hairId"], "pattern": r["pattern"],
                 "current": r["hexes"], "source": r["source"]}
        if row["id"] in MEASURED:
            char, roles = MEASURED[row["id"]]
            ch = meas.get(char, {})
            targets = {}
            deltas = {}
            for role, zone in roles.items():
                mval = (ch.get(zone) or {}).get("value")
                targets[role] = mval
                deltas[role] = delta(r["hexes"].get(role), mval)
            shade = None
            for z in ("shade", "shadow", "brown_dark"):
                if ch.get(z):
                    shade = ch[z]["value"]
                    break
            worst = max((d["max"] for d in deltas.values() if d), default=None)
            vorder = ["exact", "match", "close", "GAP"]
            ov = "no-measurement" if worst is None else max(
                (verdict(d) for d in deltas.values() if d), key=vorder.index)
            entry.update({"measured_char": char, "measured_roles": roles, "target_lit": targets,
                          "delta": deltas, "measured_shade": shade,
                          "verdict": ov})
        else:
            entry.update({"note": UNMEASURED[row["id"]], "verdict": "no-measurement"})
            report["unmeasured"].append({"look": row["id"], "name": row["name"], "note": UNMEASURED[row["id"]]})
        report["hair"].append(entry)

    # --- face decals
    # The look row's own FaceId is what the runtime actually draws; FACE_BY_LOOK is only the expectation it
    # must meet. Reading the parsed code value (not the table) is what stops a blank row from being reported
    # as wired just because a decal png exists.
    face_ids = set()
    unwired = []
    for row in rows:
        code_face = row["faceId"] or ""
        expect = FACE_BY_LOOK.get(row["id"], "")
        if not code_face and expect:
            unwired.append(row["id"])
        fid = code_face or expect
        if not fid:
            continue
        if fid in face_ids:
            continue
        face_ids.add(fid)
        report["faces"].append({"faceId": fid, "forLooks": [row["id"]],
                                "wired": bool(code_face),
                                "file": fid in face_files,
                                "mouth": (fid + "_mouth") in face_files,
                                "body": (fid + "_body") in face_files})
    report["face_unwired"] = unwired
    # a decal png with no canon look row here (e.g. face_weevil_bc, yami_marik): inventory it, flag it unwired
    for fid in sorted(face_files):
        if (fid.endswith(("_body", "_mouth", "_forehead")) or
                any(e["faceId"] == fid for e in report["faces"])):
            continue
        report["faces"].append({"faceId": fid, "forLooks": [],
                                "wired": False,
                                "file": True,
                                "mouth": (fid + "_mouth") in face_files,
                                "body": (fid + "_body") in face_files})

    # --- clothes inventory (per canon look id)
    cloth_dir = os.path.join(RES, "Clothes")
    cloth_files = {}
    if os.path.isdir(cloth_dir):
        for f in os.listdir(cloth_dir):
            if f.endswith(".png"):
                cloth_files[f] = os.path.getsize(os.path.join(cloth_dir, f))
    for row in rows:
        slots = {}
        for slot, key in (("shirt", "shirtId"), ("bottoms", "bottomsId"),
                          ("shoes", "shoesId"), ("hands", "handsId"), ("hat", "hatId")):
            name = row.get(key)
            if not name:
                continue
            fn = name + ".png"
            sz = cloth_files.get(fn)
            stub = cloth_files.get("albedo_" + name + ".png")
            slots[slot] = {"file": name, "exists": sz is not None,
                           "bytes": sz, "placeholder": (sz is not None and sz < 500),
                           "stubOnly": sz is None and stub is not None}
        report["clothes"][row["id"]] = slots

    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "canon_texture_audit.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1, ensure_ascii=False)

    # --- markdown
    L = []
    L.append("# Canon duelist texture audit (Phase 1)\n")
    L.append("Generated by `Tools/anime/audit.py`. Sources: `Assets/Scripts/WRLDZ/Data/CharacterLooks.cs`, "
             "`AiDuelistLooks.cs`, and the anime-frame measurements in `Tools/anime/measured/hair_summary.json` "
             "(method + citations: `Tools/anime/MEASUREMENT_NOTES.md`).\n")
    L.append("**Current** = the colour the game actually shows (resolved from the code, not assumed). "
             "**Target** = the daylight lit fill measured from an anime frame. "
             "Δ = target − current per channel. verdict: `exact` 0, `match` ≤6, `close` ≤20, `GAP` >20.\n")

    L.append("\n## 1. Hair\n")
    L.append("| Look | Style | Pattern | Current | Target (measured lit) | Δ (max) | Verdict |")
    L.append("|---|---|---|---|---|---|---|")
    for e in report["hair"]:
        cur = " ".join(f"{z}={e['current'].get(z)}" for z in ("root", "tip", "fringe", "streak", "side")
                       if e["current"].get(z)) or "—"
        if "target_lit" in e:
            tgt = " ".join(f"{z}={v}" for z, v in e["target_lit"].items())
            dm = max((d["max"] for d in e["delta"].values() if d), default=None)
            L.append(f"| `{e['look']}` | {e['hairStyle']} | {e['pattern']} | {cur} | {tgt} | {dm} | {e['verdict']} |")
        else:
            L.append(f"| `{e['look']}` | {e['hairStyle']} | {e['pattern']} | {cur} | — | — | no-measurement |")

    L.append("\n### Detail (per zone)\n")
    for e in report["hair"]:
        if "target_lit" not in e:
            L.append(f"- `{e['look']}` — {e['note']}")
            continue
        parts = []
        for role in e["target_lit"]:
            d = e["delta"][role]
            ds = f"Δ{d['dr']:+d}/{d['dg']:+d}/{d['db']:+d}" if d else "Δ—"
            parts.append(f"{role}: {e['current'].get(role)} → {e['target_lit'][role]} ({ds})")
        src = ", ".join(f"{z} from {e['source'].get(z)}" for z in e["target_lit"])
        L.append(f"- `{e['look']}` ({e['measured_char']}): " + "; ".join(parts)
                 + f"  [shade {e['measured_shade']}; {src}]")

    L.append("\n### Open decisions these deltas imply (Phase 2)\n")
    notemap = {
        "rex_bc": "two zones (brown crown + lavender bangs): needs a non-Original pattern so both show — "
                  "Tips root=#4D3020 tip=#8D7DB3, or TriColor root=#4D3020 fringe=#8D7DB3 tip=#4D3020 (builder's choice)",
        "mako_bc": "anime draws Mako's hair as a dark base (*near-black purple in BC art*) with blue-violet streaks; "
                   "the measured base #3C394C / streak #595E8F are DK-daylight — pattern Streaks to show both, or keep "
                   "Solid and note that the streak is invisible",
        "mokuba_bc": "measured base #1E1E1E plus blue highlight band #4C557E (his signature look): pattern Streaks "
                     "root=#1E1E1E streak=#4C557E, or keep Original and paint the band into the texture",
        "yugi_bc": "BC rows wear the yugi_muto style in its own catalogue palette (#231F2E/#EE1166/#CEBD42); the measured "
                   "DK tricolour is #131212/#9C1843/#C0B84E. Yami reads #141411/#AA1454/#CBC050 — MEASUREMENT_NOTES 1.6: "
                   "'not decidable' whether a paint difference or shot lighting, so one palette can serve both",
    }
    for lid, note in notemap.items():
        L.append(f"- `{lid}`: {note}")

    L.append("\n## 2. No anime measurement yet (Phase 1 gaps)\n")
    for u in report["unmeasured"]:
        L.append(f"- `{u['look']}` — {u['note']}")

    L.append("\n## 3. Face decals\n")
    L.append("`wired` = the look row sets FaceId (what the runtime draws). `no` = the decal png exists but the "
             "row leaves the face blank, so the blank base face is drawn instead.\n")
    L.append("| faceId | for looks | wired | png | mouth | body |")
    L.append("|---|---|---|---|---|---|")
    for f in report["faces"]:
        L.append(f"| {f['faceId']} | {', '.join(f['forLooks']) or '—'} | "
                 f"{'yes' if f.get('wired') else 'no'} | "
                 f"{'yes' if f['file'] else 'MISSING'} | "
                 f"{'yes' if f['mouth'] else '—'} | {'yes' if f['body'] else '—'} |")
    if report.get("face_unwired"):
        L.append(f"\n**Unwired ({len(report['face_unwired'])})** — decal exists, row has `FaceId` blank: "
                 + ", ".join(f"`{x}`" for x in report["face_unwired"]))
    else:
        L.append("\nEvery canon look row wires its face decal.")

    L.append("\n## 4. Garment albedos (per canon look)\n")
    L.append("`placeholder` = the png is under 500 bytes (an 8×8 stub, not a real albedo).\n")
    L.append("| Look | shirt | bottoms | shoes | hands | hat |")
    L.append("|---|---|---|---|---|---|")
    for lid, slots in report["clothes"].items():
        def cell(s):
            v = slots.get(s)
            if not v:
                return "—"
            if v["placeholder"]:
                return f"{v['file']} **(stub)**"
            if not v["exists"]:
                return f"{v['file']} **stub-only (albedo_*)**" if v["stubOnly"] else f"{v['file']} **MISSING**"
            return f"{v['file']}"
        L.append(f"| `{lid}` | {cell('shirt')} | {cell('bottoms')} | {cell('shoes')} | {cell('hands')} | {cell('hat')} |")

    with open(os.path.join(OUT, "canon_texture_audit.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")

    # stdout: compact summary
    gaps = [e["look"] for e in report["hair"] if e.get("verdict") in ("GAP", "close")]
    print(f"hair rows: {len(report['hair'])}  (GAP/close: {len(gaps)}: {' '.join(gaps)})")
    print(f"no measurement: {len(report['unmeasured'])}  faces: {len(report['faces'])}  looks: {len(report['clothes'])}")
    print(f"face decals not wired: {len(report.get('face_unwired', []))}  {report.get('face_unwired')}")
    print(f"wrote {OUT}/canon_texture_audit.md + .json")


if __name__ == "__main__":
    main()

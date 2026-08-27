#!/usr/bin/env python3
"""Extract SIMPLE continuous effects from local YGOPro/EDOPro Lua.

Authority: ~/ygopro-scripts (Fluorohydride) and optional Ignis official/.
We do NOT copy Lua into Unity — only structured facts (passcode, ATK delta,
attribute/race filter, direct-attack grant, extra-attack grant).

Skipped (too dynamic for a static catalog):
  SetValue(function), bitmasks, counters, LP-based values.
"""
from __future__ import annotations

import json
import os
import re
import sys
from collections import Counter
from pathlib import Path

ATTR = {
    "ATTRIBUTE_EARTH": "EARTH",
    "ATTRIBUTE_WATER": "WATER",
    "ATTRIBUTE_FIRE": "FIRE",
    "ATTRIBUTE_WIND": "WIND",
    "ATTRIBUTE_LIGHT": "LIGHT",
    "ATTRIBUTE_DARK": "DARK",
    "ATTRIBUTE_DIVINE": "DIVINE",
}
RACE = {
    "RACE_WARRIOR": "Warrior",
    "RACE_SPELLCASTER": "Spellcaster",
    "RACE_FAIRY": "Fairy",
    "RACE_FIEND": "Fiend",
    "RACE_ZOMBIE": "Zombie",
    "RACE_MACHINE": "Machine",
    "RACE_AQUA": "Aqua",
    "RACE_PYRO": "Pyro",
    "RACE_ROCK": "Rock",
    "RACE_WINDBEAST": "Winged Beast",
    "RACE_PLANT": "Plant",
    "RACE_INSECT": "Insect",
    "RACE_THUNDER": "Thunder",
    "RACE_DRAGON": "Dragon",
    "RACE_BEAST": "Beast",
    "RACE_BEASTWARRIOR": "Beast-Warrior",
    "RACE_DINOSAUR": "Dinosaur",
    "RACE_FISH": "Fish",
    "RACE_SEASERPENT": "Sea Serpent",
    "RACE_REPTILE": "Reptile",
    "RACE_PSYCHO": "Psychic",
    "RACE_DIVINE": "Divine-Beast",
    "RACE_WYRM": "Wyrm",
    "RACE_CYBERSE": "Cyberse",
    "RACE_ILLUSION": "Illusion",
}

UMI_CODE = 22702055

RE_ID = re.compile(r"c(\d+)\.lua$")
RE_FN_ATTR = re.compile(
    r"function\s+(\w+)\s*\([^)]*\)\s*"
    r"return\s+c:IsAttribute\((\w+)\)",
    re.I,
)
RE_FN_RACE = re.compile(
    r"function\s+(\w+)\s*\([^)]*\)\s*"
    r"return\s+c:IsRace\((\w+)\)",
    re.I,
)
RE_ENV = re.compile(r"IsEnvironment\((\d+)\)")
RE_CODE_LIST_UMI = re.compile(r"AddCodeList\([^,]+,\s*22702055\)")


def split_effects(src: str) -> list[str]:
    """Rough CreateEffect … RegisterEffect chunks, including Clone chains."""
    chunks = []
    # each RegisterEffect(eN) closes a chunk starting at the previous CreateEffect/Clone
    for m in re.finditer(
        r"((?:local\s+\w+\s*=\s*)?(?:Effect\.CreateEffect\([^)]+\)|\w+:Clone\(\))[\s\S]*?:RegisterEffect\(\w+\))",
        src,
    ):
        chunks.append(m.group(1))
    return chunks


def parse_filter(src: str, chunk: str) -> tuple[str, str]:
    """Return (attribute, race) filters; empty = all."""
    attr = ""
    race = ""
    m = re.search(r"TargetBoolFunction\(Card\.IsAttribute,\s*(\w+)\)", chunk)
    if m:
        attr = ATTR.get(m.group(1), "")
    m = re.search(r"TargetBoolFunction\(Card\.IsRace,\s*(\w+)\)", chunk)
    if m:
        race = RACE.get(m.group(1), "")
    m = re.search(r":IsAttribute\((\w+)\)", chunk)
    if m and not attr:
        attr = ATTR.get(m.group(1), "")
    m = re.search(r":IsRace\((\w+)\)", chunk)
    if m and not race:
        race = RACE.get(m.group(1), "")
    # named target function in same file
    tm = re.search(r"SetTarget\(([\w.]+)\)", chunk)
    if tm:
        fn = tm.group(1)
        am = re.search(
            rf"function\s+{re.escape(fn)}\s*\([^)]*\)\s*return\s+\w+:IsAttribute\((\w+)\)",
            src,
            re.I,
        )
        if am:
            attr = ATTR.get(am.group(1), attr)
        rm = re.search(
            rf"function\s+{re.escape(fn)}\s*\([^)]*\)\s*return\s+\w+:IsRace\((\w+)\)",
            src,
            re.I,
        )
        if rm:
            race = RACE.get(rm.group(1), race)
    return attr, race


def parse_value(chunk: str) -> int | None:
    m = re.search(r"SetValue\(\s*(-?\d+)\s*\)", chunk)
    if not m:
        return None
    return int(m.group(1))


def parse_range(chunk: str) -> str:
    m = re.search(r"SetRange\((\w+)\)", chunk)
    if not m:
        return "MZONE"
    r = m.group(1)
    if "FZONE" in r:
        return "FZONE"
    if "SZONE" in r:
        return "SZONE"
    if "GRAVE" in r:
        return "GRAVE"
    return "MZONE"


def parse_sides(chunk: str) -> tuple[bool, bool]:
    """controllerOnly, opponentOnly."""
    m = re.search(r"SetTargetRange\(([^,]+),\s*([^)]+)\)", chunk)
    if not m:
        return False, False
    a, b = m.group(1).strip(), m.group(2).strip()
    self_on = "MZONE" in a or "ONFIELD" in a
    opp_on = "MZONE" in b or "ONFIELD" in b
    if a in ("0", "nil"):
        self_on = False
    if b in ("0", "nil"):
        opp_on = False
    if self_on and not opp_on:
        return True, False
    if opp_on and not self_on:
        return False, True
    return False, False


def parse_named_condition(src: str, chunk: str) -> str:
    """Required face-up name ('Umi'), '' if unconditional, '?' if too specific."""
    hay = chunk
    cm = re.search(r"SetCondition\(([\w.]+)\)", chunk)
    if cm:
        fn = cm.group(1)
        fm = re.search(
            rf"function\s+{re.escape(fn)}\s*\([^)]*\)\s*(.*?)end",
            src,
            re.S | re.I,
        )
        if fm:
            hay = chunk + "\n" + fm.group(1)
    if "IsEnvironment(22702055)" in hay or "IsCode(22702055)" in hay or "22702055" in hay:
        return "Umi"
    if re.search(r"\bUmi\b", hay):
        return "Umi"
    if "SetCondition" in chunk:
        return "?"
    return ""


def parse_direct_requires(src: str, chunk: str) -> str:
    return parse_named_condition(src, chunk)


def extract_file(path: Path) -> tuple[list[dict], list[dict], list[dict]]:
    m = RE_ID.search(path.name)
    if not m:
        return [], [], []
    cid = int(m.group(1))
    try:
        src = path.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return [], [], []
    auras = []
    directs = []
    extras = []
    chunks = split_effects(src)

    # Clone: inherit previous numeric field ATK if this chunk is a clone
    last_field = None

    for chunk in chunks:
        is_clone = ":Clone(" in chunk
        code_atk = "EFFECT_UPDATE_ATTACK" in chunk
        code_def = "EFFECT_UPDATE_DEFENSE" in chunk
        code_dir = "EFFECT_DIRECT_ATTACK" in chunk
        code_extra = (
            "EFFECT_EXTRA_ATTACK" in chunk
            and "EFFECT_EXTRA_ATTACK_MONSTER" not in chunk
        )
        is_field = "EFFECT_TYPE_FIELD" in chunk
        is_single = "EFFECT_TYPE_SINGLE" in chunk

        if is_clone and last_field is not None:
            # clone may override code/value/target
            rec = dict(last_field)
            rec["cardId"] = cid
            val = parse_value(chunk)
            if val is not None:
                if code_def and not code_atk:
                    rec["atk"] = 0
                    rec["def"] = val
                elif code_atk and not code_def:
                    rec["atk"] = val
                    rec["def"] = 0
                elif val is not None and rec.get("atk") is not None:
                    # value override on clone of ATK effect
                    rec["atk"] = val
            attr, race = parse_filter(src, chunk)
            if attr:
                rec["attribute"] = attr
            if race:
                rec["race"] = race
            if "SetTargetRange" in chunk:
                co, oo = parse_sides(chunk)
                rec["controllerOnly"] = co
                rec["opponentOnly"] = oo
            if code_def and rec.get("atk") and not rec.get("def"):
                rec["def"] = rec["atk"]
                rec["atk"] = rec.get("atk", 0)
            # Clone of ATK that only changes SetCode to DEF
            if code_def and last_field.get("atk") and rec.get("def") == 0:
                rec["def"] = last_field["atk"]
                rec["atk"] = 0
            if rec.get("atk") or rec.get("def"):
                auras.append(rec)
                last_field = rec
            continue

        last_field = None
        if code_dir and is_single:
            req = parse_direct_requires(src, chunk)
            if req == "?":
                continue
            directs.append({"cardId": cid, "requiresName": req})
            continue

        # Continuous extra attacks (Mermaid Knight while Umi, Twinheaded Beast).
        # Skip ignition "this turn" grants (RESET_PHASE / PHASE_END, Gray Wing).
        if code_extra and is_single:
            if "RESET_PHASE" in chunk or "PHASE_END" in chunk:
                continue
            if "EFFECT_TYPE_IGNITION" in chunk:
                continue
            extra_val = parse_value(chunk)
            if extra_val is None or extra_val <= 0:
                continue
            req = parse_named_condition(src, chunk)
            if req == "?":
                continue
            extras.append({"cardId": cid, "extra": extra_val, "requiresName": req})
            continue

        val = parse_value(chunk)
        if val is None:
            continue

        if is_field and (code_atk or code_def):
            attr, race = parse_filter(src, chunk)
            co, oo = parse_sides(chunk)
            rec = {
                "cardId": cid,
                "atk": val if code_atk else 0,
                "def": val if code_def and not code_atk else (val if code_atk and code_def else 0),
                "attribute": attr,
                "race": race,
                "selfOnly": False,
                "controllerOnly": co,
                "opponentOnly": oo,
                "sourceZone": parse_range(chunk),
            }
            if code_atk and not code_def:
                rec["def"] = 0
            auras.append(rec)
            last_field = rec
        elif is_single and (code_atk or code_def) and "EFFECT_FLAG_SINGLE_RANGE" in chunk:
            rec = {
                "cardId": cid,
                "atk": val if code_atk else 0,
                "def": val if code_def else 0,
                "attribute": "",
                "race": "",
                "selfOnly": True,
                "controllerOnly": False,
                "opponentOnly": False,
                "sourceZone": parse_range(chunk),
            }
            auras.append(rec)

    return auras, directs, extras


def main() -> int:
    roots = [
        Path.home() / "ygopro-scripts",
        Path.home() / "ygo-agent/tmp_full_catalog/CardScripts/official",
    ]
    seen_ids = set()
    auras: list[dict] = []
    directs: list[dict] = []
    extras: list[dict] = []
    files = 0
    for root in roots:
        if not root.is_dir():
            continue
        for p in sorted(root.glob("c*.lua")):
            mid = RE_ID.search(p.name)
            if not mid:
                continue
            cid = int(mid.group(1))
            if cid in seen_ids:
                continue
            seen_ids.add(cid)
            files += 1
            a, d, x = extract_file(p)
            auras.extend(a)
            directs.extend(d)
            extras.extend(x)

    # Dedup identical rows
    def key_a(r):
        return (
            r["cardId"], r["atk"], r["def"], r.get("attribute", ""),
            r.get("race", ""), r.get("selfOnly"), r.get("controllerOnly"),
            r.get("opponentOnly"), r.get("sourceZone"),
        )

    uniq_a, seen_a = [], set()
    for r in auras:
        k = key_a(r)
        if k in seen_a:
            continue
        seen_a.add(k)
        uniq_a.append(r)
    uniq_d, seen_d = [], set()
    for r in directs:
        k = (r["cardId"], r.get("requiresName", ""))
        if k in seen_d:
            continue
        seen_d.add(k)
        uniq_d.append(r)
    uniq_x, seen_x = [], set()
    for r in extras:
        k = (r["cardId"], r.get("extra", 0), r.get("requiresName", ""))
        if k in seen_x:
            continue
        seen_x.add(k)
        uniq_x.append(r)

    out = {
        "source": "ygopro-scripts + ProjectIgnis official (behavior facts only, not Lua)",
        "scriptFiles": files,
        "auraCount": len(uniq_a),
        "directCount": len(uniq_d),
        "extraCount": len(uniq_x),
        "auras": uniq_a,
        "directAttacks": uniq_d,
        "extraAttacks": uniq_x,
    }
    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
        "/home/greatsaiyadave/DMWRLDZUnityProject/ProjectARGON/"
        "Assets/StreamingAssets/WRLDZ/ygopro_continuous_seed_v1.json"
    )
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print(f"wrote {dest}")
    print(f"files={files} auras={len(uniq_a)} directs={len(uniq_d)} extras={len(uniq_x)}")
    # sanity
    star = [a for a in uniq_a if a["cardId"] == 8201910]
    mk3 = [d for d in uniq_d if d["cardId"] == 64342551]
    mermaid = [x for x in uniq_x if x["cardId"] == 24435369]
    twin = [x for x in uniq_x if x["cardId"] == 82035781]
    print("Star Boy auras", star)
    print("MK-3 directs", mk3)
    print("Mermaid Knight extras", mermaid)
    print("Twinheaded Beast extras", twin)
    by_zone = Counter(a.get("sourceZone") for a in uniq_a)
    print("by zone", dict(by_zone))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

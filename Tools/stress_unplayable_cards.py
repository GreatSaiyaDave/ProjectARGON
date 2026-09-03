#!/usr/bin/env python3
"""Stress: Equips / Continuous / Field in cards_db vs compiler-shaped regexes.

Exit 1 when a *simple* template Equip (no leftover rider) is not matched — those
must FullyCompile in C#. Unique leftover Equips are listed as expected refuse.
Does not invent resolutions.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"

RX_EQUIP_LEGACY = re.compile(
    r"A \w+(?:-Type)? monster equipped with this card increases?",
    re.I,
)
RX_EQUIP_INCREASE = re.compile(
    r"Increase the ATK and DEF of a \w+(?:-Type)? monster equipped with this card",
    re.I,
)
RX_EQUIP_ONLY = re.compile(
    r"Equip only to (?:an? )?[A-Za-z]+(?:-(?!Type)[A-Za-z]+)*(?:-Type)? monster\.?\s*"
    r"It gains \d+ ATK(?:/DEF| and DEF| and loses \d+ DEF)?\.?\s*$",
    re.I,
)
RX_EQUIP_GAINS = re.compile(
    r"The equipped monster gains \d+ ATK(?:/DEF| and DEF| and loses \d+ DEF)?\.?\s*$",
    re.I,
)
RX_BOILER_ALWAYS = re.compile(
    r"\(This card(?:'s name)? is always treated as [^)]+\)\.?\s*",
    re.I,
)

MUST_PLAY = {
    1435851: "Dragon Treasure",
    32268901: "Salamandra",
    61854111: "Legendary Sword",
    25769732: "Machine Conversion Factory",
    51267887: "Raise Body Heat",
    77007920: "Laser Cannon Armor",
    77027445: "Power of Kaishin",
}


def load_cards() -> list[dict]:
    raw = json.loads(CARDS.read_text(encoding="utf-8"))
    return raw["cards"] if isinstance(raw, dict) else raw


def is_equip(c: dict) -> bool:
    kind = (c.get("type") or "") + " " + (c.get("race") or "") + " " + (c.get("frameType") or "")
    return "Equip" in kind and "Spell" in (c.get("type") or "")


def strip_boiler(desc: str) -> str:
    return RX_BOILER_ALWAYS.sub("", desc or "").strip()


def simple_equip(desc: str) -> bool:
    t = strip_boiler(desc)
    t = re.sub(r"\s+", " ", t).strip()
    return bool(
        RX_EQUIP_LEGACY.search(t)
        or RX_EQUIP_INCREASE.search(t)
        or RX_EQUIP_ONLY.match(t)
        or RX_EQUIP_GAINS.match(t)
    )


def main() -> int:
    if not CARDS.is_file():
        print(f"missing {CARDS}", file=sys.stderr)
        return 1
    cards = load_cards()
    equips = [c for c in cards if is_equip(c)]
    simple = []
    leftover = []
    unique = []
    for c in equips:
        desc = c.get("desc") or ""
        if simple_equip(desc):
            simple.append(c)
        elif RX_EQUIP_ONLY.search(strip_boiler(desc)) or RX_EQUIP_GAINS.search(strip_boiler(desc)) or RX_EQUIP_LEGACY.search(desc):
            leftover.append(c)
        else:
            unique.append(c)

    gaps = []
    by_id = {int(c["id"]): c for c in cards if c.get("id")}
    for cid, name in MUST_PLAY.items():
        c = by_id.get(cid)
        if c is None:
            continue
        if not simple_equip(c.get("desc") or ""):
            gaps.append(f"{cid} {name}: simple Equip template missed official text")

    print(f"equips in cards_db: {len(equips)}")
    print(f"simple template (must FullyCompile): {len(simple)}")
    for c in simple:
        print(f"  PLAY  {c['id']} {c.get('name')}")
    print(f"template + leftover rider (refuse OK): {len(leftover)}")
    for c in leftover:
        print(f"  REFUSE {c['id']} {c.get('name')}")
    print(f"unique Equip (refuse OK): {len(unique)}")
    for c in unique:
        print(f"  UNIQUE {c['id']} {c.get('name')}")

    # Continuous / Field: count only — C# stress covers activation.
    cont = [c for c in cards if "Continuous" in ((c.get("race") or "") + (c.get("type") or ""))]
    field = [c for c in cards if "Field" in (c.get("race") or "") and "Spell" in (c.get("type") or "")]
    print(f"continuous S/T in cards_db: {len(cont)}")
    print(f"field spells in cards_db: {len(field)}")
    print("master spec: unique leftover Equips stay unimplemented; simple templates must play")

    if gaps:
        print(f"--- {len(gaps)} gap(s) ---")
        for g in gaps:
            print("FAIL  " + g)
        return 1
    print("WRLDZ_EQUIP_STRESS_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

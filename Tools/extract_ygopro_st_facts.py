#!/usr/bin/env python3
"""Mine SIMPLE Continuous/Equip/Field Spell-Trap facts from official Lua.

Facts only — we do not copy or run Lua. Product path compiles cards_db text.
Skip coin, tokens, SetValue(function), negate-all, archetypes.

Primary root: vendored OcgCore/scripts/official, then Project Ignis official/.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
LUA_ROOTS = [
    ROOT / "Assets/StreamingAssets/OcgCore/scripts/official",
    Path.home() / "Games/ProjectIgnis/script/official",
    Path.home() / "ygopro-scripts",
]

RE_ID = re.compile(r"c(\d+)\.lua$")
RACE = {
    "RACE_INSECT": "Insect",
    "RACE_WARRIOR": "Warrior",
    "RACE_SPELLCASTER": "Spellcaster",
    "RACE_DRAGON": "Dragon",
    "RACE_MACHINE": "Machine",
    "RACE_AQUA": "Aqua",
    "RACE_ZOMBIE": "Zombie",
    "RACE_FAIRY": "Fairy",
    "RACE_BEAST": "Beast",
    "RACE_FIEND": "Fiend",
    "RACE_WINDBEAST": "Winged Beast",
    "RACE_DINOSAUR": "Dinosaur",
    "RACE_PLANT": "Plant",
    "RACE_ROCK": "Rock",
    "RACE_FISH": "Fish",
    "RACE_PYRO": "Pyro",
    "RACE_THUNDER": "Thunder",
    "RACE_REPTILE": "Reptile",
}

UNIQUE_SKIP = (
    "EFFECT_DISABLE",
    "EFFECT_DISABLE_EFFECT",
    "EFFECT_CANNOT_TRIGGER",
    "TossCoin",
    "TYPE_TOKEN",
    "CreateToken",
    "EFFECT_CHANGE_RACE",
    "EFFECT_SET_POSITION",
    "EFFECT_CANNOT_CHANGE_POSITION",
    "EFFECT_CANNOT_SUMMON",
    "EFFECT_CANNOT_SPECIAL_SUMMON",
    "EFFECT_CANNOT_ACTIVATE",
    "WIN_REASON",
)


def load_db() -> dict[int, dict]:
    raw = json.loads(CARDS.read_text(encoding="utf-8"))
    cards = raw["cards"] if isinstance(raw, dict) else raw
    return {int(c["id"]): c for c in cards if c.get("id")}


def is_st_like(card: dict) -> bool:
    t = (card.get("type") or "") + " " + (card.get("race") or "")
    if "Monster" in (card.get("type") or "") and "Spell" not in t and "Trap" not in t:
        return False
    return any(k in t for k in ("Continuous", "Equip", "Field", "Spell", "Trap"))


def skip_unique(src: str) -> bool:
    return any(s in src for s in UNIQUE_SKIP)


def parse_pay_lp(src: str) -> int | None:
    m = re.search(r"Cost\.PayLP\((\d+)\)", src)
    if m:
        return int(m.group(1))
    m = re.search(r"PayLPCost\(\w+,\s*(\d+)\)", src)
    if m:
        return int(m.group(1))
    m = re.search(r"CheckLPCost\(\w+,\s*(\d+)\)", src)
    if m:
        return int(m.group(1))
    return None


def parse_cannot_attack(src: str) -> dict | None:
    if "EFFECT_CANNOT_ATTACK" not in src and "EFFECT_CANNOT_ATTACK_ANNOUNCE" not in src:
        return None
    if "SetValue(function" in src:
        return None
    rec: dict = {"atkMin": 0, "levelMin": 0, "race": "", "side": "both"}
    if re.search(r"SetTargetRange\(\s*0\s*,\s*LOCATION_MZONE\s*\)", src):
        rec["side"] = "opponent"
    elif re.search(r"SetTargetRange\(\s*LOCATION_MZONE\s*,\s*0\s*\)", src):
        rec["side"] = "controller"
    m = re.search(r"GetLevel\(\)\s*>=\s*(\d+)", src)
    if m:
        rec["levelMin"] = int(m.group(1))
    m = re.search(r"GetAttack\(\)\s*>=\s*(\d+)", src)
    if m:
        rec["atkMin"] = int(m.group(1))
    m = re.search(r"IsRace\((\w+)\)", src)
    if m:
        rec["race"] = RACE.get(m.group(1), m.group(1))
    if rec["levelMin"] == 0 and rec["atkMin"] == 0 and not rec["race"]:
        return None
    return rec


def parse_phase(src: str) -> dict | None:
    end = "EVENT_PHASE+PHASE_END" in src or "EVENT_PHASE|PHASE_END" in src
    standby = "EVENT_PHASE+PHASE_STANDBY" in src or "EVENT_PHASE|PHASE_STANDBY" in src
    if not end and not standby:
        return None
    if "TRIGGER_F" not in src and "TRIGGER_O" not in src and "EFFECT_TYPE_CONTINUOUS" not in src:
        return None
    rec = {
        "phase": "end" if end else "standby",
        "turnPlayer": "Duel.GetTurnPlayer" in src or "IsTurnPlayer" in src or "EFFECT_FLAG_BOTH_SIDE" in src,
        "actionHint": "",
    }
    if "CATEGORY_RELEASE" in src or "SelectReleaseGroup" in src:
        rec["actionHint"] = "tribute"
    elif "CATEGORY_POSITION" in src or "ChangePosition" in src:
        rec["actionHint"] = "change_position"
    elif "CATEGORY_DAMAGE" in src or "Duel.Damage" in src:
        rec["actionHint"] = "damage"
    elif "PayLPCost" in src and "Destroy" in src:
        rec["actionHint"] = "pay_or_destroy"
    return rec


def parse_gy_revive(src: str) -> dict | None:
    if "CATEGORY_SPECIAL_SUMMON" not in src:
        return None
    if "LOCATION_GRAVE" not in src:
        return None
    if "EVENT_LEAVE_FIELD" not in src:
        return None
    defense = "POS_FACEUP_DEFENSE" in src or "POS_DEFENSE" in src
    normal = "TYPE_NORMAL" in src or "IsType(TYPE_NORMAL)" in src
    return {"defense": defense, "normalOnly": normal}


def classify(src: str, card: dict) -> dict | None:
    if skip_unique(src):
        return None
    fact: dict = {"cardId": int(card["id"]), "name": card.get("name") or ""}
    play = (
        "EFFECT_TYPE_ACTIVATE" in src
        and "EVENT_FREE_CHAIN" in src
    )
    if play:
        fact["playAsActivation"] = True
    pay = None
    if play:
        # Pay on the activate effect only (Toon World). Ignore later maintain costs
        # unless there is no other operation on Activate.
        act_m = re.search(
            r"EFFECT_TYPE_ACTIVATE[\s\S]{0,400}?Cost\.PayLP\((\d+)\)",
            src,
        )
        if act_m:
            pay = int(act_m.group(1))
            fact["activatePayLp"] = pay
    ca = parse_cannot_attack(src)
    if ca:
        fact["cannotAttack"] = ca
    ph = parse_phase(src)
    if ph:
        fact["phaseTrigger"] = ph
        if ph.get("actionHint") == "pay_or_destroy":
            n = parse_pay_lp(src)
            if n:
                fact["standbyPayOrDestroy"] = n
    gy = parse_gy_revive(src)
    if gy:
        fact["gyReviveLeaveField"] = gy
    keys = [k for k in fact if k not in ("cardId", "name")]
    if not keys:
        return None
    return fact


def main() -> int:
    db = load_db()
    seen: set[int] = set()
    facts: list[dict] = []
    files = 0
    skipped_unique = 0
    for root in LUA_ROOTS:
        if not root.is_dir():
            continue
        for p in sorted(root.glob("c*.lua")):
            m = RE_ID.search(p.name)
            if not m:
                continue
            cid = int(m.group(1))
            if cid in seen:
                continue
            card = db.get(cid)
            if card is None:
                continue
            t = (card.get("type") or "") + " " + (card.get("race") or "")
            if "Continuous" not in t and "Equip" not in t and "Field" not in t:
                continue
            seen.add(cid)
            files += 1
            src = p.read_text(encoding="utf-8", errors="replace")
            if skip_unique(src):
                skipped_unique += 1
                continue
            rec = classify(src, card)
            if rec:
                facts.append(rec)

    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else (
        ROOT / "Assets/StreamingAssets/WRLDZ/ygopro_st_facts_v1.json"
    )
    out = {
        "source": "OcgCore/scripts/official then ProjectIgnis official (facts only, not Lua)",
        "scriptFilesScanned": files,
        "skippedUnique": skipped_unique,
        "factCount": len(facts),
        "facts": facts,
    }
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {dest} facts={len(facts)} scanned={files} uniqueSkip={skipped_unique}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

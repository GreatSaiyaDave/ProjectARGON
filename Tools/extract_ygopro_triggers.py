#!/usr/bin/env python3
"""Mine SIMPLE trap/QE trigger facts from YGOPro Lua for cards in cards_db.

Facts only — we do not copy or run Lua. Skips coin flips, random-hand, named
archetype locks, and SetValue(function) operations.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
LUA_ROOTS = [
    Path.home() / "ygopro-scripts",
    Path.home() / "ygo-agent/tmp_full_catalog/CardScripts/official",
]

RE_ID = re.compile(r"c(\d+)\.lua$")
RACE = {
    "RACE_FIEND": "Fiend",
    "RACE_WARRIOR": "Warrior",
    "RACE_SPELLCASTER": "Spellcaster",
    "RACE_DRAGON": "Dragon",
    "RACE_MACHINE": "Machine",
    "RACE_AQUA": "Aqua",
    "RACE_ZOMBIE": "Zombie",
    "RACE_FAIRY": "Fairy",
    "RACE_BEAST": "Beast",
    "RACE_WINDBEAST": "Winged Beast",
    "RACE_DINOSAUR": "Dinosaur",
    "RACE_INSECT": "Insect",
    "RACE_PLANT": "Plant",
    "RACE_ROCK": "Rock",
    "RACE_FISH": "Fish",
    "RACE_SEASERPENT": "Sea Serpent",
    "RACE_REPTILE": "Reptile",
    "RACE_PYRO": "Pyro",
    "RACE_THUNDER": "Thunder",
}


def load_db() -> dict[int, dict]:
    raw = json.loads(CARDS.read_text(encoding="utf-8"))
    cards = raw["cards"] if isinstance(raw, dict) else raw
    return {int(c["id"]): c for c in cards if c.get("id")}


def classify(src: str, card: dict) -> dict | None:
    """Return one simple one-shot trap/QE trigger, or None."""
    kind = (card.get("type") or "") + " " + (card.get("race") or "")
    is_trap = "Trap" in kind
    is_quick = "Quick-Play" in kind
    if not is_trap and not is_quick:
        return None
    if "Continuous" in kind or "Equip" in kind:
        return None
    if "EFFECT_TYPE_ACTIVATE" not in src:
        return None
    if any(s in src for s in (
        "TossCoin", "TYPE_EQUIP", "EquipLimit", "EFFECT_EQUIP",
        "EVENT_CHAINING", "SUMMON_TYPE_ADVANCE", "LOCATION_REMOVED",
    )):
        return None

    attack = "SetCode(EVENT_ATTACK_ANNOUNCE)" in src or "SetCode(EVENT_ATTACK_ANNOUNCE)" in src.replace(" ", "")
    if "EVENT_ATTACK_ANNOUNCE" in src:
        attack = True
    dmg = (
        ("EFFECT_FLAG_DAMAGE_STEP" in src or "TIMING_DAMAGE_STEP" in src or "PHASE_DAMAGE" in src)
        and not attack
    )
    summon = ("EVENT_SUMMON_SUCCESS" in src or "EVENT_FLIP_SUMMON_SUCCESS" in src) and not attack

    rec: dict = {
        "timing": "",
        "action": "",
        "amount": 0,
        "lpMultiple": 0,
        "alsoDef": False,
        "requiresControllerRace": "",
        "opponentTurn": True,
    }

    if attack:
        rec["timing"] = "attack_announce"
        # Only the simple Mirror Force pattern: destroy every attack-position monster
        # the opponent controls. Skip "highest ATK" / tribute-summoned / extra damage.
        if "Duel.NegateAttack" in src and "SkipPhase" in src:
            rec["action"] = "negate_attack_end_bp"
            return rec
        if "Duel.NegateAttack" in src and "Duel.Damage" in src:
            rec["action"] = "negate_attack_damage"
            return rec
        if "Duel.NegateAttack" in src and "Duel.Recover" in src:
            rec["action"] = "negate_attack_gain_lp"
            return rec
        if "Duel.Remove" in src and "POS_DEFENSE" in src or (
            "Duel.Remove" in src and "IsDefensePos" in src
        ):
            rec["action"] = "banish_opp_def_pos"
            return rec
        if "GetAttacker" in src and "Duel.Destroy" in src and "IsAttackPos" not in src:
            rec["action"] = "destroy_attacker"
            return rec
        if "IsAttackPos" in src and "Duel.Destroy" in src and "GetAttack" not in src.replace("GetAttacker", ""):
            rec["action"] = "destroy_opp_atk_pos"
            return rec
        return None

    if dmg:
        rec["timing"] = "damage_step"
        rec["opponentTurn"] = False  # either player if their monster battles
        race_m = re.search(r"IsRace\((\w+)\)", src)
        if race_m:
            rec["requiresControllerRace"] = RACE.get(race_m.group(1), "")
        if "PayLPCost" in src or "CheckLPCost" in src:
            rec["action"] = "lose_atk_def_pay_lp"
            rec["lpMultiple"] = 100
            rec["alsoDef"] = True
            return rec
        vm = re.search(r"SetValue\(\s*(-?\d+)\s*\)", src)
        if vm and "EFFECT_UPDATE_ATTACK" in src and "GetAttacker" in src:
            rec["action"] = "lose_atk_eot"
            rec["amount"] = abs(int(vm.group(1)))
            rec["alsoDef"] = "EFFECT_UPDATE_DEFENSE" in src
            return rec
        return None

    if summon:
        rec["timing"] = "summon_success"
        # Classic Trap Hole: destroy the summoned monster if ATK ≥ N. Skip mass-wipe.
        if "Duel.Destroy" in src and "GetMatchingGroup" not in src:
            rec["action"] = "destroy_summoned"
            atk = re.search(r"GetAttack\(\)\s*>=\s*(\d+)", src) or re.search(
                r"GetAttack\(\)>=(\d+)", src
            )
            rec["amount"] = int(atk.group(1)) if atk else 1000
            return rec
        return None

    return None


def main() -> int:
    db = load_db()
    seen: set[int] = set()
    rows: list[dict] = []
    files = 0
    for root in LUA_ROOTS:
        if not root.is_dir():
            continue
        for p in sorted(root.glob("c*.lua")):
            m = RE_ID.search(p.name)
            if not m:
                continue
            cid = int(m.group(1))
            if cid not in db or cid in seen:
                continue
            seen.add(cid)
            files += 1
            try:
                src = p.read_text(encoding="utf-8", errors="ignore")
            except OSError:
                continue
            fact = classify(src, db[cid])
            if not fact or not fact.get("action"):
                continue
            fact["cardId"] = cid
            rows.append(fact)

    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / (
        "Assets/StreamingAssets/WRLDZ/ygopro_trigger_seed_v1.json"
    )
    dest.parent.mkdir(parents=True, exist_ok=True)
    out = {
        "source": "ygopro-scripts (behavior facts only, not Lua) restricted to cards_db.json",
        "scriptFilesScanned": files,
        "triggerCount": len(rows),
        "triggers": rows,
    }
    dest.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print(f"wrote {dest}")
    print(f"scanned={files} triggers={len(rows)}")
    by_t: dict[str, int] = {}
    by_a: dict[str, int] = {}
    for r in rows:
        by_t[r["timing"]] = by_t.get(r["timing"], 0) + 1
        by_a[r["action"]] = by_a.get(r["action"], 0) + 1
    print("by timing", by_t)
    print("by action", by_a)
    for cid in (41925941, 56120475, 44095762, 62279055, 14315573, 57882509, 43250041, 77754944):
        hit = [r for r in rows if r["cardId"] == cid]
        print(f"  {cid}", hit)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""
Offline Phase 1 bulk compile (no Unity lock required).
Mirrors CardTextEffectCompiler regex templates → StreamingAssets seed + coverage report.

Usage:
  python3 Tools/WRLDZ/phase1_bulk_compile.py
"""
from __future__ import annotations

import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
DECKS = ROOT / "Assets/StreamingAssets/Decks"
SEED = ROOT / "Assets/StreamingAssets/WRLDZ/compiled_effects_seed_v1.json"
REPORT = Path(__file__).resolve().parent / "effect_coverage_phase1_latest.txt"

DECK_FILES = [
    "lab_rules_player.json",
    "lab_rules_ai.json",
    "player_starter.json",
    "starter_junkuriboh.json",
    "starter_kuribandit.json",
    "starter_galactikuriboh.json",
    "ai_kaiba.json",
]

# Registry IDs (OfficialEffectRegistry + MonsterEffects hard-coded)
REGISTRY = {
    55144522, 53129443, 12580477, 19613556, 5318639, 83764719, 72892473,
    24094653, 72302403, 44095762, 4206964, 12607053, 14315573, 83555666,
    98045062, 43973174,
    54652250, 31560081, 26202165, 40640057, 34124316, 17985575,
}

# EffectTiming / EffectActionKind numeric order must match C# enums
class Timing:
    None_ = 0
    Activate = 1
    Flip = 2
    SentFromFieldToGy = 3
    AttackDeclared = 4
    OpponentNormalOrFlipSummon = 5
    ContinuousWhileFaceUp = 6
    DamageCalculation = 7

class Action:
    None_ = 0
    Draw = 1
    Destroy = 2
    SpecialSummonFromGy = 3
    SpecialSummonFromHand = 4
    AddFromGyToHand = 5
    AddFromDeckToHand = 6
    ChangeBattlePosition = 7
    NegateAttack = 8
    EndBattlePhase = 9
    ApplyWabokuStyle = 10
    ApplySwordsOfRevealingLight = 11
    BothPlayersDiscardAndRedraw = 12
    FusionSummonRegistered = 13
    EffectDamageBothFromOriginalAtk = 14
    CyberJarStyle = 15
    ContinuousCannotTargetDragons = 16
    DiscardSelfNoBattleDamageThisBattle = 17

class Side:
    Controller = 0
    Opponent = 1
    Both = 2
    Either = 3

class Zone:
    None_ = 0
    FieldMonsters = 1
    FieldSpellTraps = 2
    OppAttackPositionMonsters = 3
    OppFaceUpMonsters = 4
    EitherGyMonsters = 5
    ControllerGySpells = 6
    ControllerHandDragons = 7
    DeckMonstersAtkLeq = 8
    FieldAnyMonster = 9
    AttackingMonster = 10


PATTERNS = [
    (re.compile(r'FLIP:\s*Destroy all monsters on the field, then both players reveal the top (\d+) cards from their Decks', re.I),
     lambda m: clause(Timing.Flip, Action.CyberJarStyle, amount=int(m.group(1)))),
    (re.compile(r"After this card's activation, it remains on the field, but you must destroy it during the End Phase of your opponent's (\d+)(?:rd|nd|th|st)? turn\.", re.I),
     lambda m: clause(Timing.Activate, Action.ApplySwordsOfRevealingLight, amount=int(m.group(1)), stays=True)),
    (re.compile(r"During your opponent's turn:\s*Target 1 face-up monster your opponent controls whose ATK is less than or equal to their LP;\s*destroy that face-up monster, and if you do, take damage equal to its original ATK, then inflict damage to your opponent, equal to the damage you took", re.I),
     lambda m: clause(Timing.Activate, Action.EffectDamageBothFromOriginalAtk, zone=Zone.OppFaceUpMonsters, target=True, opp_turn=True)),
    (re.compile(r"You take no battle damage from your opponent's monsters this turn\.\s*Your monsters cannot be destroyed by battle this turn\.?", re.I),
     lambda m: clause(Timing.Activate, Action.ApplyWabokuStyle)),
    (re.compile(r"During damage calculation, if your opponent's monster attacks(?:\s*\(Quick Effect\))?:\s*You can discard this card;\s*you take no battle damage from that battle\.?", re.I),
     lambda m: clause(Timing.DamageCalculation, Action.DiscardSelfNoBattleDamageThisBattle, side=Side.Controller)),
    (re.compile(r"Both players discard as many cards as possible from their hands, then each player draws the same number of cards they discarded\.?", re.I),
     lambda m: clause(Timing.Activate, Action.BothPlayersDiscardAndRedraw)),
    (re.compile(r"Destroy all monsters your opponent controls\.?", re.I),
     lambda m: clause(Timing.Activate, Action.Destroy, side=Side.Opponent, zone=Zone.FieldMonsters)),
    (re.compile(r"Destroy all monsters on the field\.?", re.I),
     lambda m: ("dark_hole", m)),
    (re.compile(r"Destroy all Spell and Trap Cards on the field\.?", re.I),
     lambda m: clause(Timing.Activate, Action.Destroy, side=Side.Both, zone=Zone.FieldSpellTraps)),
    (re.compile(r"Target 1 Spell/?Trap on the field;\s*destroy that target\.?", re.I),
     lambda m: clause(Timing.Activate, Action.Destroy, zone=Zone.FieldSpellTraps, target=True)),
    (re.compile(r"Target 1 monster in either GY;\s*Special Summon it\.?", re.I),
     lambda m: clause(Timing.Activate, Action.SpecialSummonFromGy, zone=Zone.EitherGyMonsters, target=True)),
    (re.compile(r"Draw (\d+) cards?\.", re.I),
     lambda m: clause(Timing.Activate, Action.Draw, side=Side.Controller, amount=int(m.group(1)))),
    (re.compile(r"FLIP:\s*Target 1 monster on the field;\s*destroy it\.?", re.I),
     lambda m: clause(Timing.Flip, Action.Destroy, zone=Zone.FieldAnyMonster, target=True)),
    (re.compile(r"FLIP:\s*Target 1 Spell in your GY;\s*add that target to your hand\.?", re.I),
     lambda m: clause(Timing.Flip, Action.AddFromGyToHand, zone=Zone.ControllerGySpells, target=True)),
    (re.compile(r"When your opponent Normal or Flip Summons 1 monster with (\d+) or more ATK:\s*Target that monster;\s*destroy that target\.?", re.I),
     lambda m: clause(Timing.OpponentNormalOrFlipSummon, Action.Destroy, zone=Zone.FieldAnyMonster, amount=int(m.group(1)), target=True)),
    (re.compile(r"When an opponent's monster declares an attack:\s*Destroy all your opponent's Attack Position monsters\.?", re.I),
     lambda m: clause(Timing.AttackDeclared, Action.Destroy, side=Side.Opponent, zone=Zone.OppAttackPositionMonsters)),
    (re.compile(r"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*negate the attack, then end the Battle Phase\.?", re.I),
     lambda m: clause(Timing.AttackDeclared, Action.NegateAttack, zone=Zone.AttackingMonster)),
    (re.compile(r"If this card is sent from the field to the GY:\s*Add 1 monster with (\d+) or less ATK from your Deck to your hand", re.I),
     lambda m: clause(Timing.SentFromFieldToGy, Action.AddFromDeckToHand, zone=Zone.DeckMonstersAtkLeq, amount=int(m.group(1)))),
    (re.compile(r'Special Summon up to (\d+) Dragon monsters? from your hand\.\s*"Lord of D\.?" must be on the field', re.I),
     lambda m: clause(Timing.Activate, Action.SpecialSummonFromHand, zone=Zone.ControllerHandDragons, amount=int(m.group(1)), lord=True)),
    (re.compile(r"Fusion Summon 1 Fusion Monster from your Extra Deck, using monsters from your hand or field as Fusion Material\.?", re.I),
     lambda m: clause(Timing.Activate, Action.FusionSummonRegistered)),
    (re.compile(r"Neither player can target Dragon monsters on the field with card effects\.?", re.I),
     lambda m: clause(Timing.ContinuousWhileFaceUp, Action.ContinuousCannotTargetDragons)),
    (re.compile(r"Target 1 face-up monster your opponent controls;\s*change that target's battle position", re.I),
     lambda m: clause(Timing.Activate, Action.ChangeBattlePosition, zone=Zone.OppFaceUpMonsters, target=True)),
]

SWORDS_ABSORB = [
    re.compile(r"While this card is face-up on the field, your opponent's monsters cannot declare an attack\.?", re.I),
    re.compile(r"When this card is activated:\s*If your opponent controls a face-down monster, flip all monsters they control face-up\.?", re.I),
]


def clause(timing, action, side=Side.Opponent, zone=Zone.None_, amount=0, target=False, stays=False, opp_turn=False, lord=False, snippet=""):
    return {
        "Timing": timing,
        "Action": action,
        "Side": side,
        "Zone": zone,
        "Amount": amount,
        "RequiresTargetChoice": target,
        "RequiresLordOfDOnField": lord,
        "OpponentTurnOnly": opp_turn,
        "StaysOnField": stays,
        "SourceSnippet": snippet,
    }


def normalize(s: str) -> str:
    s = s.replace("\r", " ").replace("\n", " ")
    return re.sub(r"\s+", " ", s).strip()


def text_hash(t: str) -> str:
    if not t:
        return "empty"
    h = hashlib.sha256(t.encode("utf-8")).digest()
    return "".join(f"{b:02x}" for b in h[:8])


def is_normal(card: dict) -> bool:
    t = card.get("type") or ""
    return (
        "monster" in t.lower()
        and "normal monster" in t.lower()
        and "effect" not in t.lower()
        and "pendulum" not in t.lower()
    )


def is_extra_deck(card: dict) -> bool:
    t = (card.get("type") or "").lower()
    f = (card.get("frameType") or "").lower()
    keys = ("fusion", "synchro", "xyz", "link")
    return any(k in t or f == k for k in keys)


def has_no_activatable_effect(card: dict) -> bool:
    """Normal monsters + effectless Extra Deck (classic Fusions: BSD, Gaia Champion)."""
    if is_normal(card):
        return True
    t = card.get("type") or ""
    f = card.get("frameType") or ""
    if "monster" not in t.lower():
        return False
    if not is_extra_deck(card):
        return False
    # Explicit Effect → needs a script
    if "effect" in t.lower() or "effect" in f.lower():
        return False
    return True


def is_boilerplate(frag: str) -> bool:
    f = frag.lower()
    if "you can only activate 1" in f:
        return True
    if "you can only use" in f:
        return True
    if "this card is always treated as" in f:
        return True
    if f.startswith("●"):
        return True
    if "tribute 1 monster, then target" in f:
        return True
    if "cannot activate cards, or the effects" in f:
        return True
    if len(f) < 8:
        return True
    return False


def compile_card(card: dict) -> dict:
    cid = int(card.get("id") or 0)
    name = card.get("name") or f"#{cid}"
    desc = card.get("desc") or ""
    th = text_hash(desc)
    prog = {
        "CardId": cid,
        "CardName": name,
        "TextHash": th,
        "SourceText": desc,
        "Clauses": [],
        "FullyCompiled": False,
        "UnparsedFragments": [],
        "CompiledUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "CompilerVersion": 1,
        "CompileSource": "regex",
    }
    if has_no_activatable_effect(card):
        prog["FullyCompiled"] = True
        prog["CompileSource"] = "normal" if is_normal(card) else "structural"
        return prog
    if not desc.strip():
        return prog

    text = normalize(desc)
    matched = []
    clauses = []

    # special dark hole: only if not opponent-only destroy already
    has_opp_destroy = False

    for rx, builder in PATTERNS:
        m = rx.search(text)
        if not m:
            continue
        built = builder(m)
        if isinstance(built, tuple) and built[0] == "dark_hole":
            if has_opp_destroy:
                continue
            c = clause(Timing.Activate, Action.Destroy, side=Side.Both, zone=Zone.FieldMonsters)
            c["SourceSnippet"] = m.group(0).strip()
            clauses.append(c)
            matched.append((m.start(), m.end() - m.start()))
            continue
        if isinstance(built, dict):
            built["SourceSnippet"] = m.group(0).strip()
            if built.get("Action") == Action.Destroy and built.get("Side") == Side.Opponent:
                has_opp_destroy = True
            if built.get("Action") == Action.ApplySwordsOfRevealingLight:
                for arx in SWORDS_ABSORB:
                    am = arx.search(text)
                    if am:
                        matched.append((am.start(), am.end() - am.start()))
            clauses.append(built)
            matched.append((m.start(), m.end() - m.start()))

    # mask matched
    chars = list(text)
    for start, length in matched:
        for i in range(start, min(start + length, len(chars))):
            chars[i] = " "
    remaining = "".join(chars)
    unparsed = []
    for part in re.split(r"(?<=[.!?])\s+", remaining):
        t = part.strip()
        if len(t) > 2 and not is_boilerplate(t):
            unparsed.append(t)

    prog["Clauses"] = clauses
    prog["UnparsedFragments"] = unparsed
    prog["FullyCompiled"] = (len(unparsed) == 0 and len(clauses) > 0)
    return prog


def load_deck_ids() -> set[int]:
    ids: set[int] = set()
    for name in DECK_FILES:
        path = DECKS / name
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        for key in ("main", "extra", "side"):
            for e in data.get(key) or []:
                if isinstance(e, dict) and e.get("id"):
                    ids.add(int(e["id"]))
                elif isinstance(e, int):
                    ids.add(e)
    return ids


def main() -> int:
    if not CARDS.exists():
        print("cards_db missing", CARDS, file=sys.stderr)
        return 1
    cards_data = json.loads(CARDS.read_text(encoding="utf-8"))
    by_id = {int(c["id"]): c for c in cards_data.get("cards") or [] if c.get("id")}
    ids = sorted(load_deck_ids())
    print(f"Pool unique ids: {len(ids)}")

    programs = []
    stats = {
        "normal": 0,
        "full": 0,
        "partial": 0,
        "registry": 0,
        "gap": 0,
        "missing": 0,
    }
    lines = [
        "═══ EFFECT COVERAGE — starter_and_lab (offline Phase 1) ═══",
        f"Generated: {datetime.now(timezone.utc).isoformat()}Z",
        f"Compiler v1 (python mirror of CardTextEffectCompiler)",
        "",
    ]
    gaps = []

    for cid in ids:
        card = by_id.get(cid)
        if not card:
            stats["missing"] += 1
            lines.append(f"  MISS     {cid}")
            continue
        prog = compile_card(card)
        programs.append(prog)
        name = prog["CardName"]
        reg = cid in REGISTRY
        if is_normal(card):
            stats["normal"] += 1
            stats["full"] += 1
            lines.append(f"  NORMAL   {cid} «{name}»")
        elif has_no_activatable_effect(card):
            stats["normal"] += 1
            stats["full"] += 1
            lines.append(f"  STRUCT   {cid} «{name}» (effectless Extra/Fusion)")
        elif prog["FullyCompiled"]:
            stats["full"] += 1
            lines.append(
                f"  FULL     {cid} «{name}» clauses={len(prog['Clauses'])}"
            )
        elif reg:
            stats["registry"] += 1
            lines.append(f"  REGISTRY {cid} «{name}» (hard-coded script)")
        elif prog["Clauses"]:
            stats["partial"] += 1
            lines.append(
                f"  PARTIAL  {cid} «{name}» clauses={len(prog['Clauses'])}"
            )
            gaps.append((cid, name, "partial"))
        else:
            stats["gap"] += 1
            lines.append(f"  GAP      {cid} «{name}»")
            gaps.append((cid, name, "gap"))

    in_db = len(ids) - stats["missing"]
    playable = stats["full"] + stats["registry"]
    denom = max(1, in_db)
    gate = "PASS" if playable >= in_db and stats["gap"] == 0 else "FAIL"
    summary = [
        "",
        f"Pool «starter_and_lab» unique={len(ids)}",
        f"  inDB={in_db} miss={stats['missing']} normal/structural={stats['normal']} "
        f"fullText={stats['full'] - stats['normal']} partial={stats['partial']} "
        f"registry={stats['registry']} gap={stats['gap']}",
        f"  playableCovered={playable}/{in_db} ({100*playable/denom:.1f}%)  "
        f"fullCompile%={100*stats['full']/denom:.1f}%",
        "  playable = normal + effectless Fusion/Extra + FullyCompiled text + registry scripts",
        f"MASTERY GATE: {gate} playable={100*playable/denom:.1f}% gap={stats['gap']}",
    ]
    if gaps:
        summary.append("  Priority gaps (first 40):")
        for cid, name, kind in gaps[:40]:
            summary.append(f"    · {cid} «{name}» [{kind}]")

    lines.extend(summary)
    report = "\n".join(lines) + "\n"
    REPORT.write_text(report, encoding="utf-8")

    SEED.parent.mkdir(parents=True, exist_ok=True)
    seed = {"version": 1, "programs": programs}
    SEED.write_text(json.dumps(seed, indent=4), encoding="utf-8")

    print(report)
    print(f"Seed → {SEED} ({len(programs)} programs)")
    print(f"Report → {REPORT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

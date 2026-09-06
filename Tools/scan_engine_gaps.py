#!/usr/bin/env python3
"""Offline gap scan: official cards_db text vs compiler regex vs YGOPro seed.

Catches Mermaid Knight-class misses (continuous extra attack / Umi direct)
without waiting for a live duel. Does not invent effects — it only flags
mechanics this engine already claims to support.

Exit 1 when a simple extra-attack or direct-attack line in cards_db is
missing from both the compiler regex and the Lua seed.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
SEED = ROOT / "Assets/StreamingAssets/WRLDZ/ygopro_continuous_seed_v1.json"
TRIGGERS = ROOT / "Assets/StreamingAssets/WRLDZ/ygopro_trigger_seed_v1.json"
ST_FACTS = ROOT / "Assets/StreamingAssets/WRLDZ/ygopro_st_facts_v1.json"
PUZZLE_FACTS = ROOT / "Assets/StreamingAssets/WRLDZ/puzzle_facts_v1.json"
PRE_LINK_SETS = ROOT / "Assets/StreamingAssets/WRLDZ/eras/pre_link_sets.json"

# Must stay in lockstep with CardTextEffectCompiler extra/direct regexes.
RX_EXTRA_NAMED = re.compile(
    r'(?:While|As long as) "([^"]+)" (?:is|remains)(?: face-up)? on the field, '
    r"this card can attack twice during the same Battle Phase\.?",
    re.I,
)
RX_EXTRA_ANY = re.compile(
    r"This card can attack twice during the same Battle Phase\.?",
    re.I,
)
RX_DIRECT_NAMED = re.compile(
    r'(?:While|As long as) "([^"]+)" (?:is|remains)(?: face-up)? on the field, '
    r"this card can attack (?:your opponent(?:'s Life Points)? )?directly\.?",
    re.I,
)
RX_DIRECT_ANY = re.compile(
    r"This (?:card|monster) (?:may|can(?!not)) attack "
    r"(?:your opponent(?:'s Life Points)? )?directly"
    r"(?: even if there is a monster on your opponent's side of the field)?\.?",
    re.I,
)
RX_EXTRA_TEXT = re.compile(r"attack twice during the same Battle Phase", re.I)
RX_THIS_TURN_EXTRA = re.compile(r"Battle Phase of this turn", re.I)
RX_UMI_WHILE = re.compile(r'While "Umi" is (?:face-up )?on the field', re.I)
RX_SAKURETSU = re.compile(
    r"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*destroy that target",
    re.I,
)
RX_BARK = re.compile(
    r"If a \w+(?:-Type)? monster you control battles, during the Damage Step:\s*"
    r"Pay LP \(in multiples of \d+ points\)",
    re.I,
)
RX_MAGIC_CYL = re.compile(
    r"When an opponent's monster declares an attack:\s*Target the attacking monster;\s*"
    r"negate the attack, and if you do, inflict damage",
    re.I,
)
RX_ATTACK_DECLARE = re.compile(
    r"When an opponent's monster declares an attack:",
    re.I,
)
RX_FIELD_AS_UMI = re.compile(r'the field is treated as "Umi"', re.I)
RX_TORNADO = re.compile(r'Activate only while "Umi" is on the field', re.I)
RX_GRANADORA = re.compile(
    r"Normal Summoned, Flip Summoned or Special Summoned, increase your Life Points",
    re.I,
)
RX_DAEDALUS = re.compile(
    r'(?:You can )?send 1 face-up "([^"]+)" you control to the (?:GY|Graveyard);\s*'
    r"destroy all other cards on the field",
    re.I,
)
RX_IGN_SET_FD = re.compile(
    r"You can change this card to face-down Defense Position",
    re.I,
)
RX_IGN_TRIBUTE_DMG = re.compile(
    r"Tribute \d+ monsters?;\s*inflict \d+ damage to your opponent",
    re.I,
)
RX_IGN_TRIBUTE_THIS_DESTROY = re.compile(
    r"Tribute this card to target 1 monster on the field;\s*destroy that target",
    re.I,
)
RX_IGN_BANISH_TARGET = re.compile(
    r"target 1 face-up monster on the field;\s*banish that target",
    re.I,
)
RX_IGN_DISCARD_SELF_ADD = re.compile(
    r"discard this card;\s*add ",
    re.I,
)
RX_COIN_CALL = re.compile(r"toss a coin and call it", re.I)
RX_SPELL_COUNTER = re.compile(r"place \d+ spell counter", re.I)
RX_TOKEN_SS = re.compile(r'special summon \d+ "[^"]*token"', re.I)
RX_EXTRA_FUSION = re.compile(r"fusion monster from your extra deck", re.I)
RX_TAKE_CONTROL = re.compile(r"take control of all face-up level", re.I)
RX_STANDBY_DMG = re.compile(r"during your standby phase:\s*take \d+ damage", re.I)
RX_STANDBY_LP_EACH = re.compile(
    r"increase your Life Points by \d+ points during each of your Standby Phases",
    re.I,
)
RX_TRIBUTE_NAMED_DESTROY = re.compile(
    r'By Tributing 1 "[^"]+" on your side of the field, destroy 1',
    re.I,
)
RX_SUIJIN_ATK0 = re.compile(
    r"During damage calculation in your opponent's turn, if this card is being attacked",
    re.I,
)
RX_SS_BANISH_ATTR_GY = re.compile(
    r"Special Summoned by removing from play \d+ \w+ monsters? in your Graveyard",
    re.I,
)
RX_SKIP_OPP_DRAW = re.compile(
    r"skips their next Draw Phase",
    re.I,
)
RX_FISHERMAN = re.compile(
    r'(?:While|As long as) "([^"]+)" is(?: face-up)? on the field, this card is '
    r"unaffected by (?:any )?(?:Spell|Trap|monster) (?:Cards|effects) and cannot be "
    r"targeted for attacks",
    re.I,
)
RX_UNAFFECTED_NAMED = re.compile(
    r'(?:While|As long as) "([^"]+)" is(?: face-up)? on the field, this card is '
    r"unaffected by (?:any )?(?:Spell|Trap|monster) (?:Cards|effects)",
    re.I,
)
RX_EQUIP_ATK = re.compile(
    r"A \w+(?:-Type)? monster equipped with this card increases?",
    re.I,
)
RX_EQUIP_ONLY = re.compile(
    r"Equip only to (?:an? )?[A-Za-z]+(?:-(?!Type)[A-Za-z]+)*(?:-Type)? monster\.?\s*"
    r"It gains \d+ ATK",
    re.I,
)
RX_EQUIP_GAINS = re.compile(
    r"The equipped monster gains \d+ ATK",
    re.I,
)
RX_FALLING_DOWN = re.compile(
    r"Activate this card by targeting an opponent's monster;\s*"
    r"equip this card to it\.\s*Take control of it",
    re.I,
)
RX_SECOND_ATTACK = re.compile(
    r"This card can make a second attack during each Battle Phase",
    re.I,
)
RX_BOOK_MOON = re.compile(
    r"change that target to face-down Defense Position",
    re.I,
)
RX_INCREASE_LP = re.compile(r"Increase your Life Points by \d+ points", re.I)
RX_ABSOLUTE_END = re.compile(
    r"Activate only during your opponent's turn\.\s*"
    r"This turn, the attacks from your opponent's monsters become direct attacks",
    re.I,
)
RX_WHEN_TAKE_DAMAGE = re.compile(
    r"You can only activate this card when you take damage to your Life Points",
    re.I,
)
RX_TERRAFORMING = re.compile(
    r"Add 1 Field Spell(?: Card)? from your Deck to your hand",
    re.I,
)
RX_ROTA = re.compile(
    r"Add 1 Level \d+ or lower \w+(?:-Type)? monster from your Deck to your hand",
    re.I,
)
RX_INFLICT_OPP = re.compile(
    r"Inflict \d+ (?:points of )?damage to your opponent",
    re.I,
)
RX_DESTROY_OPP_ST = re.compile(
    r"Destroy all Spell and Trap Cards your opponent controls",
    re.I,
)
RX_ECTO = re.compile(
    r"Once per turn, during each player's End Phase:\s*"
    r"The turn player must Tribute 1 face-up monster",
    re.I,
)
RX_TOON_PAY = re.compile(
    r"Activate this card by paying \d+ (?:LP|Life Points)",
    re.I,
)
RX_LABYRINTH = re.compile(
    r"during each player's End Phase:\s*"
    r"Change the battle positions of all face-up monsters the turn player controls",
    re.I,
)
RX_BURNING_LAND = re.compile(
    r"When this card is activated:\s*If there are any Field Spell Cards on the field, destroy them",
    re.I,
)
RX_GRAVITY_BIND = re.compile(
    r"Level \d+ or higher monsters cannot attack",
    re.I,
)
RX_INSECT_BARRIER = re.compile(
    r"\w+(?:-Type)? monsters your opponent controls cannot declare an attack",
    re.I,
)
RX_CANNOT_ATTACK_ATK = re.compile(
    r"Monsters with \d+ or more ATK cannot declare an attack",
    re.I,
)
RX_PAY_OR_DESTROY = re.compile(
    r"during your Standby Phase,? pay \d+ (?:LP|Life Points) or destroy this card",
    re.I,
)
RX_CDBB = re.compile(
    r"(?:^|(?<=[.!?]\s)|\"[^\"]+\"(?:\s*\+\s*\"[^\"]+\")+\s+)"
    r"(?:Cannot be destroyed by battle(?! with| or)|"
    r"This card (?:cannot be destroyed by battle(?! with| or)|is not destroyed as a result of battle))\.",
    re.I,
)
RX_PREMATURE = re.compile(
    r"Activate this card by paying \d+ (?:LP|Life Points), then target 1 monster in your (?:GY|Graveyard);"
    r"\s*Special Summon that target in Attack Position and equip it with this card",
    re.I,
)
RX_TRIBUTE_SUMMON_DESTROY_MONSTER = re.compile(
    r"this card is Tribute Summoned:\s*Target 1 monster on the field;\s*destroy that target",
    re.I,
)
RX_CALL_HAUNTED = re.compile(
    r"Activate this card by targeting 1 monster in your (?:GY|Graveyard);\s*"
    r"Special Summon",
    re.I,
)
RX_SOUL_RES = re.compile(
    r"Activate this card by targeting 1 Normal Monster in your (?:GY|Graveyard);\s*"
    r"Special Summon",
    re.I,
)

UNIQUE_ST_LEFTOVER = {
    3136426,  # Level Limit - Area B
    21770260,  # Jam Breeding Machine
    82732705,  # Skill Drain
    82003859,  # Toll
    94212438,  # Destiny Board
    74701381,  # DNA Surgery
    93016201,  # Royal Oppression
    17078030,  # Wall of Revealing Light
}


def load_cards() -> list[dict]:
    raw = json.loads(CARDS.read_text(encoding="utf-8"))
    return raw["cards"] if isinstance(raw, dict) else raw


def original_set_codes() -> set[str]:
    """Original ERAZ wall: pre_link set codes with order strictly before TLM.

    Used when ``--era original`` is passed so the gap scan excludes TLM
    (GX-first) passcodes. Default scan stays full cards_db.
    """
    if not PRE_LINK_SETS.is_file():
        return set()
    raw = json.loads(PRE_LINK_SETS.read_text(encoding="utf-8"))
    sets = raw.get("sets") or []
    tlm_order = None
    for s in sets:
        if s and str(s.get("code") or "").upper() == "TLM":
            tlm_order = s.get("order")
            break
    if tlm_order is None:
        return set()
    out: set[str] = set()
    for s in sets:
        if not s:
            continue
        code = str(s.get("code") or "").strip()
        if not code or code.upper() == "TLM":
            continue
        order = s.get("order")
        if order is not None and order < tlm_order:
            out.add(code.upper())
    return out


def tlm_passcodes() -> set[int]:
    """Passcodes listed under TLM in pre_link_sets (skipped for --era original)."""
    if not PRE_LINK_SETS.is_file():
        return set()
    raw = json.loads(PRE_LINK_SETS.read_text(encoding="utf-8"))
    for s in raw.get("sets") or []:
        if s and str(s.get("code") or "").upper() == "TLM":
            return {int(x) for x in (s.get("priorityPasscodes") or []) if x}
    return set()


def load_seed() -> dict:
    if not SEED.is_file():
        return {}
    return json.loads(SEED.read_text(encoding="utf-8"))


def load_st_facts() -> dict[int, dict]:
    if not ST_FACTS.is_file():
        return {}
    raw = json.loads(ST_FACTS.read_text(encoding="utf-8"))
    out: dict[int, dict] = {}
    for t in raw.get("facts") or []:
        if t and t.get("cardId"):
            out[int(t["cardId"])] = t
    return out


def load_puzzle_activate_ids() -> set[int]:
    if not PUZZLE_FACTS.is_file():
        return set()
    raw = json.loads(PUZZLE_FACTS.read_text(encoding="utf-8"))
    ids: set[int] = set()
    for p in raw.get("puzzles") or []:
        for a in p.get("activate") or []:
            if a and a.get("cardId"):
                ids.add(int(a["cardId"]))
    return ids


def load_triggers() -> dict[int, dict]:
    if not TRIGGERS.is_file():
        return {}
    raw = json.loads(TRIGGERS.read_text(encoding="utf-8"))
    out: dict[int, dict] = {}
    for t in raw.get("triggers") or []:
        if t and t.get("cardId"):
            out[int(t["cardId"])] = t
    return out


def compiler_extra(desc: str) -> tuple[bool, str]:
    m = RX_EXTRA_NAMED.search(desc or "")
    if m:
        return True, m.group(1)
    if RX_EXTRA_ANY.search(desc or ""):
        return True, ""
    return False, ""


def compiler_direct(desc: str) -> tuple[bool, str]:
    m = RX_DIRECT_NAMED.search(desc or "")
    if m:
        return True, m.group(1)
    if RX_DIRECT_ANY.search(desc or ""):
        return True, ""
    return False, ""


def index_seed(seed: dict) -> tuple[dict[int, list], dict[int, list], dict[int, list]]:
    auras: dict[int, list] = {}
    directs: dict[int, list] = {}
    extras: dict[int, list] = {}
    for a in seed.get("auras") or []:
        auras.setdefault(int(a["cardId"]), []).append(a)
    for d in seed.get("directAttacks") or []:
        directs.setdefault(int(d["cardId"]), []).append(d)
    for e in seed.get("extraAttacks") or []:
        extras.setdefault(int(e["cardId"]), []).append(e)
    return auras, directs, extras


def parse_args(argv: list[str] | None = None) -> tuple[str | None, list[str]]:
    argv = list(sys.argv[1:] if argv is None else argv)
    era = None
    rest: list[str] = []
    i = 0
    while i < len(argv):
        if argv[i] == "--era" and i + 1 < len(argv):
            era = argv[i + 1].strip().lower()
            i += 2
            continue
        if argv[i].startswith("--era="):
            era = argv[i].split("=", 1)[1].strip().lower()
            i += 1
            continue
        rest.append(argv[i])
        i += 1
    return era, rest


def main() -> int:
    era, _ = parse_args()
    if era is not None and era != "original":
        print(f"FAIL  unknown --era {era!r} (supported: original)")
        return 2

    if not CARDS.is_file():
        print(f"FAIL  missing {CARDS}")
        return 1
    cards = load_cards()
    skip_ids: set[int] = set()
    if era == "original":
        orig_codes = original_set_codes()
        skip_ids = tlm_passcodes()
        print("== WRLDZ engine gap scan (era=original; TLM skipped) ==")
        print(f"original_set_codes: {len(orig_codes)} ({', '.join(sorted(orig_codes))})")
        print(f"tlm passcodes skipped: {len(skip_ids)}")
    else:
        print("== WRLDZ engine gap scan ==")

    seed = load_seed()
    _, seed_dir, seed_x = index_seed(seed)
    trig = load_triggers()
    st_facts = load_st_facts()
    puzzle_act = load_puzzle_activate_ids()
    print(f"cards_db: {len(cards)}")
    print(
        f"seed: auras={seed.get('auraCount', 0)} "
        f"directs={seed.get('directCount', 0)} "
        f"extras={seed.get('extraCount', len(seed.get('extraAttacks') or []))} "
        f"scripts={seed.get('scriptFiles', 0)}"
    )
    print(f"trigger seed: {len(trig)}")
    print(f"st facts: {len(st_facts)}")
    print(f"puzzle activate ids: {len(puzzle_act)}")

    gaps: list[str] = []
    covered_extra = 0
    covered_direct = 0
    umi_info: list[str] = []

    for c in cards:
        cid = int(c.get("id") or 0)
        if cid and cid in skip_ids:
            continue
        name = c.get("name") or "?"
        desc = c.get("desc") or ""
        if not desc:
            continue

        kind = (c.get("type") or "") + " " + (c.get("race") or "")

        extra_ok, extra_req = compiler_extra(desc)
        if extra_ok:
            covered_extra += 1
            tag = "compiler"
            if cid in seed_x:
                tag += "+seed"
            print(f"COVERED extra-attack  {cid} {name}  {tag}  requires={extra_req or '-'}")
            if extra_req == "Umi" and cid in seed_x and not any(
                (e.get("requiresName") or "") == "Umi" for e in seed_x.get(cid, [])
            ):
                gaps.append(
                    f"{cid} {name}: extra-attack requires Umi but seed has {seed_x.get(cid)}"
                )

        elif RX_EXTRA_TEXT.search(desc) and not RX_THIS_TURN_EXTRA.search(desc):
            gaps.append(
                f"{cid} {name}: official text grants extra attack but compiler regex missed it"
            )

        direct_ok, direct_req = compiler_direct(desc)
        if direct_ok:
            covered_direct += 1
            tag = "compiler"
            if cid in seed_dir:
                tag += "+seed"
            print(f"COVERED direct-attack {cid} {name}  {tag}  requires={direct_req or '-'}")

        if RX_FIELD_AS_UMI.search(desc):
            print(f"COVERED field-as-Umi     {cid} {name}  compiler")
        if RX_TORNADO.search(desc):
            print(f"COVERED tornado-wall     {cid} {name}  compiler")
        if RX_GRANADORA.search(desc):
            print(f"COVERED granadora        {cid} {name}  compiler")
        if RX_DAEDALUS.search(desc):
            print(f"COVERED send-named-destroy-other {cid} {name}  compiler")
        if RX_IGN_SET_FD.search(desc):
            print(f"COVERED ignition-set-fd      {cid} {name}  compiler")
        if RX_IGN_TRIBUTE_DMG.search(desc):
            print(f"COVERED ignition-tribute-dmg {cid} {name}  compiler")
        if RX_IGN_TRIBUTE_THIS_DESTROY.search(desc):
            print(f"COVERED ignition-tribute-this-destroy {cid} {name}  compiler")
        if RX_IGN_BANISH_TARGET.search(desc):
            print(f"COVERED ignition-banish-target {cid} {name}  compiler")
        if RX_IGN_DISCARD_SELF_ADD.search(desc):
            print(f"COVERED ignition-discard-self-add {cid} {name}  compiler")
        if RX_TRIBUTE_NAMED_DESTROY.search(desc):
            print(f"COVERED tribute-named-destroy {cid} {name}  compiler")
        if RX_SUIJIN_ATK0.search(desc):
            print(f"COVERED suijin-atk0          {cid} {name}  compiler")
        if RX_SS_BANISH_ATTR_GY.search(desc):
            print(f"COVERED ss-banish-attr-gy    {cid} {name}  compiler")
        if RX_SKIP_OPP_DRAW.search(desc):
            print(f"COVERED skip-opp-draw        {cid} {name}  compiler")
        if RX_COIN_CALL.search(desc):
            print(f"COVERED coin-call            {cid} {name}  compiler")
        if RX_SPELL_COUNTER.search(desc):
            print(f"COVERED spell-counter        {cid} {name}  compiler")
        if RX_TOKEN_SS.search(desc):
            print(f"COVERED token-ss             {cid} {name}  compiler")
        if RX_EXTRA_FUSION.search(desc):
            print(f"COVERED extra-fusion-ss      {cid} {name}  compiler")
        if RX_TAKE_CONTROL.search(desc):
            print(f"COVERED take-control         {cid} {name}  compiler")
        if RX_STANDBY_DMG.search(desc):
            print(f"COVERED standby-damage       {cid} {name}  compiler")
        if RX_STANDBY_LP_EACH.search(desc):
            print(f"COVERED standby-gain-lp      {cid} {name}  compiler")
        if RX_FISHERMAN.search(desc):
            print(f"COVERED umi-protection       {cid} {name}  compiler")
        elif RX_UNAFFECTED_NAMED.search(desc):
            print(f"COVERED named-unaffected     {cid} {name}  compiler")
        if RX_EQUIP_ATK.search(desc):
            print(f"COVERED equip-atk            {cid} {name}  compiler")
        if RX_SECOND_ATTACK.search(desc):
            print(f"COVERED second-attack        {cid} {name}  compiler")
        if RX_BOOK_MOON.search(desc):
            print(f"COVERED book-of-moon         {cid} {name}  compiler")
        if RX_INCREASE_LP.search(desc) and "Spell" in kind:
            print(f"COVERED increase-lp          {cid} {name}  compiler")
        if RX_ABSOLUTE_END.search(desc):
            print(f"COVERED opp-turn-direct      {cid} {name}  compiler")
        if RX_WHEN_TAKE_DAMAGE.search(desc):
            print(f"COVERED when-take-damage     {cid} {name}  compiler")
        if RX_TERRAFORMING.search(desc) and "Spell" in kind:
            print(f"COVERED field-spell-search   {cid} {name}  compiler")
        if RX_ROTA.search(desc) and "Spell" in kind:
            print(f"COVERED level-race-search    {cid} {name}  compiler")
        if RX_INFLICT_OPP.search(desc) and "Spell" in kind and "Trap" not in kind:
            print(f"COVERED inflict-opp          {cid} {name}  compiler")
        if RX_DESTROY_OPP_ST.search(desc):
            print(f"COVERED destroy-opp-st       {cid} {name}  compiler")
        if RX_ECTO.search(desc):
            print(f"COVERED ectoplasmer-phase    {cid} {name}  compiler")
        if RX_TOON_PAY.search(desc):
            print(f"COVERED activate-pay-lp      {cid} {name}  compiler")
        if RX_LABYRINTH.search(desc):
            print(f"COVERED labyrinth-positions  {cid} {name}  compiler")
        if RX_BURNING_LAND.search(desc):
            print(f"COVERED burning-land         {cid} {name}  compiler")
        if RX_GRAVITY_BIND.search(desc):
            print(f"COVERED gravity-bind         {cid} {name}  compiler")
        if RX_INSECT_BARRIER.search(desc):
            print(f"COVERED insect-barrier       {cid} {name}  compiler")
        if RX_CANNOT_ATTACK_ATK.search(desc):
            print(f"COVERED cannot-attack-atk    {cid} {name}  compiler")
        if RX_PAY_OR_DESTROY.search(desc):
            print(f"COVERED standby-pay-or-destroy {cid} {name}  compiler")
        if RX_CALL_HAUNTED.search(desc) or RX_SOUL_RES.search(desc):
            print(f"COVERED gy-revive-leave      {cid} {name}  compiler")
        if RX_PREMATURE.search(desc):
            print(f"COVERED premature-gy-equip   {cid} {name}  compiler")
        if RX_CDBB.search(desc):
            print(f"COVERED cannot-destroy-battle {cid} {name}  compiler")
        if RX_TRIBUTE_SUMMON_DESTROY_MONSTER.search(desc):
            print(f"COVERED tribute-summon-destroy {cid} {name}  compiler")

        fact = st_facts.get(cid)
        if fact and cid not in UNIQUE_ST_LEFTOVER:
            ca = fact.get("cannotAttack") or {}
            if ca.get("levelMin") and not RX_GRAVITY_BIND.search(desc):
                if re.search(r"Level \d+ or higher monsters cannot attack", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua cannot-attack-by-level but compiler regex missed it"
                    )
            if ca.get("race") and "opponent" in str(ca.get("side")) and not RX_INSECT_BARRIER.search(desc):
                if re.search(r"cannot declare an attack", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua opponent-race cannot-attack but compiler regex missed it"
                    )
            if ca.get("atkMin") and not RX_CANNOT_ATTACK_ATK.search(desc):
                if re.search(r"or more ATK cannot declare an attack", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua ATK cannot-attack but compiler regex missed it"
                    )
            if fact.get("activatePayLp") and not RX_TOON_PAY.search(desc):
                if re.search(r"Activate this card by paying", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua PayLP activate but compiler regex missed it"
                    )
            if (fact.get("gyReviveLeaveField") and not RX_CALL_HAUNTED.search(desc)
                    and not RX_SOUL_RES.search(desc)):
                if re.search(r"When this card leaves the field, destroy that monster", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua GY-revive-leave-field but compiler regex missed it"
                    )
            ph = fact.get("phaseTrigger") or {}
            if ph.get("actionHint") == "tribute" and not RX_ECTO.search(desc):
                if re.search(r"turn player must Tribute", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua End Phase turn-player tribute but compiler regex missed it"
                    )
            if ph.get("actionHint") == "change_position" and not RX_LABYRINTH.search(desc):
                if re.search(r"turn player controls", desc, re.I):
                    gaps.append(
                        f"{cid} {name}: Lua End Phase turn-player positions but compiler regex missed it"
                    )

        if RX_UMI_WHILE.search(desc) and not extra_ok and not direct_ok:
            umi_info.append(f"INFO  Umi-family not extra/direct  {cid} {name}")

        if "Trap" in kind:
            if RX_BARK.search(desc):
                print(f"COVERED damage-step trap {cid} {name}  compiler")
                if cid not in trig:
                    gaps.append(f"{cid} {name}: Bark-style Damage Step trap missing from trigger seed")
            elif RX_SAKURETSU.search(desc) or RX_MAGIC_CYL.search(desc):
                print(f"COVERED attack-declare trap {cid} {name}  compiler")
                if cid not in trig:
                    gaps.append(f"{cid} {name}: simple attack-declare trap missing from trigger seed")
            elif RX_ATTACK_DECLARE.search(desc) and cid not in trig:
                umi_info.append(
                    f"INFO  attack-declare trap not in simple catalog  {cid} {name}"
                )

    # Must-have cards from live-play misses
    must = {
        24435369: "Mermaid Knight extra-attack",
        82035781: "Twinheaded Beast extra-attack",
        64342551: "MK-3 direct-attack",
        8201910: "Star Boy auras (seed)",
        41925941: "Bark of Dark Ruler Damage Step",
        56120475: "Sakuretsu Armor attack-declare",
        13944422: "Granadora summon/destroy LP",
        17214465: "Maiden of the Aqua field=Umi",
        18605135: "Tornado Wall",
        37721209: "Daedalus send-Umi destroy others",
        74131780: "Exiled Force tribute-this destroy",
        11384280: "Cannon Soldier tribute damage",
        9596126: "Chaos Sorcerer banish target",
        2326738: "Des Lacooda set face-down",
        31786629: "Thunder Dragon discard-self search",
        71625222: "Time Wizard coin call",
        71413901: "Breaker Spell Counters",
        69015963: "Cyber-Stein Extra Deck SS",
        62543393: "Lekunga token",
        102380: "Lava Golem Standby damage",
        3643300: "Legendary Fisherman Umi protection",
        14087893: "Book of Moon face-down DEF",
        21015833: "Hayabusa Knight second attack",
        1435851: "Dragon Treasure Equip ATK/DEF",
        32268901: "Salamandra Equip only FIRE",
        61854111: "Legendary Sword Equip only Warrior",
        40619825: "Axe of Despair equipped-gains ATK",
        32919136: "Falling Down opponent-equip take-control",
        27744077: "Absolute End opponent-turn direct attacks",
        2130625: "Numinous Healer when you take damage",
        73628505: "Terraforming Field Spell search",
        32807846: "Reinforcement of the Army Warrior search",
        46130346: "Hinotama inflict 500",
        18144507: "Harpie's Feather Duster destroy opp S/T",
        85802526: "Cure Mermaid Standby LP gain",
        63120904: "Orca Mega-Fortress tribute-named destroy",
        98434877: "Suijin damage-calc ATK 0",
        218704: "Fenrir SS by banishing WATER from GY",
        97342942: "Ectoplasmer End Phase tribute",
        15259703: "Toon World pay-LP Activate",
        66526672: "Labyrinth of Nightmare End Phase positions",
        24294108: "Burning Land destroy Field Spells + Standby damage",
        85742772: "Gravity Bind Level 4+ cannot attack",
        23615409: "Insect Barrier opponent Insect cannot attack",
        44656491: "Messenger of Peace ATK lock + pay or destroy",
        97077563: "Call of the Haunted GY SS leave-field",
        92924317: "Soul Resurrection Normal GY SS Defense",
    }
    by_id = {
        int(c["id"]): c
        for c in cards
        if c.get("id") and int(c["id"]) not in skip_ids
    }
    if 24435369 in by_id and 24435369 not in seed_x:
        gaps.append("24435369 Mermaid Knight missing from seed extraAttacks")
    if 82035781 in by_id and 82035781 not in seed_x:
        gaps.append("82035781 Twinheaded Beast missing from seed extraAttacks")
    if 64342551 in by_id and 64342551 not in seed_dir:
        gaps.append("64342551 MK-3 missing from seed directAttacks")
    auras = (seed.get("auras") or [])
    if 8201910 in by_id and not any(int(a.get("cardId") or 0) == 8201910 for a in auras):
        gaps.append("8201910 Star Boy missing from seed auras")
    if 41925941 in by_id and 41925941 not in trig:
        gaps.append("41925941 Bark of Dark Ruler missing from trigger seed")
    if 56120475 in by_id and 56120475 not in trig:
        gaps.append("56120475 Sakuretsu Armor missing from trigger seed")
    if 37721209 in by_id and not RX_DAEDALUS.search(by_id[37721209].get("desc") or ""):
        gaps.append("37721209 Daedalus send-Umi destroy-other regex missed official text")
    if 74131780 in by_id and not RX_IGN_TRIBUTE_THIS_DESTROY.search(by_id[74131780].get("desc") or ""):
        gaps.append("74131780 Exiled Force tribute-this destroy regex missed official text")
    if 11384280 in by_id and not RX_IGN_TRIBUTE_DMG.search(by_id[11384280].get("desc") or ""):
        gaps.append("11384280 Cannon Soldier tribute damage regex missed official text")
    if 3643300 in by_id and not RX_FISHERMAN.search(by_id[3643300].get("desc") or ""):
        gaps.append("3643300 Legendary Fisherman Umi-protection regex missed official text")
    if 14087893 in by_id and not RX_BOOK_MOON.search(by_id[14087893].get("desc") or ""):
        gaps.append("14087893 Book of Moon face-down DEF regex missed official text")
    if 21015833 in by_id and not RX_SECOND_ATTACK.search(by_id[21015833].get("desc") or ""):
        gaps.append("21015833 Hayabusa Knight second-attack regex missed official text")
    if 1435851 in by_id and not RX_EQUIP_ATK.search(by_id[1435851].get("desc") or ""):
        gaps.append("1435851 Dragon Treasure equip-atk regex missed official text")
    if 32268901 in by_id and not RX_EQUIP_ONLY.search(by_id[32268901].get("desc") or ""):
        gaps.append("32268901 Salamandra equip-only regex missed official text")
    if 61854111 in by_id and not RX_EQUIP_ONLY.search(by_id[61854111].get("desc") or ""):
        gaps.append("61854111 Legendary Sword equip-only regex missed official text")
    if 40619825 in by_id and not RX_EQUIP_GAINS.search(by_id[40619825].get("desc") or ""):
        gaps.append("40619825 Axe of Despair equipped-gains regex missed official text")
    if 32919136 in by_id and not RX_FALLING_DOWN.search(by_id[32919136].get("desc") or ""):
        gaps.append("32919136 Falling Down opponent-equip regex missed official text")
    if 27744077 in by_id and not RX_ABSOLUTE_END.search(by_id[27744077].get("desc") or ""):
        gaps.append("27744077 Absolute End opponent-turn direct-attack regex missed official text")
    if 2130625 in by_id and not RX_WHEN_TAKE_DAMAGE.search(by_id[2130625].get("desc") or ""):
        gaps.append("2130625 Numinous Healer when-you-take-damage regex missed official text")
    if 73628505 in by_id and not RX_TERRAFORMING.search(by_id[73628505].get("desc") or ""):
        gaps.append("73628505 Terraforming Field Spell search regex missed official text")
    if 32807846 in by_id and not RX_ROTA.search(by_id[32807846].get("desc") or ""):
        gaps.append("32807846 Reinforcement of the Army Warrior-search regex missed official text")
    if 46130346 in by_id and not RX_INFLICT_OPP.search(by_id[46130346].get("desc") or ""):
        gaps.append("46130346 Hinotama inflict-damage regex missed official text")
    if 18144507 in by_id and not RX_DESTROY_OPP_ST.search(by_id[18144507].get("desc") or ""):
        gaps.append("18144507 Harpie's Feather Duster destroy-opp-ST regex missed official text")
    if 85802526 in by_id and not RX_STANDBY_LP_EACH.search(by_id[85802526].get("desc") or ""):
        gaps.append("85802526 Cure Mermaid Standby LP-gain regex missed official text")
    if 63120904 in by_id and not RX_TRIBUTE_NAMED_DESTROY.search(by_id[63120904].get("desc") or ""):
        gaps.append("63120904 Orca Mega-Fortress tribute-named destroy regex missed official text")
    if 98434877 in by_id and not RX_SUIJIN_ATK0.search(by_id[98434877].get("desc") or ""):
        gaps.append("98434877 Suijin damage-calc ATK 0 regex missed official text")
    if 218704 in by_id and not RX_SS_BANISH_ATTR_GY.search(by_id[218704].get("desc") or ""):
        gaps.append("218704 Fenrir SS-by-banish regex missed official text")
    if 97342942 in by_id and not RX_ECTO.search(by_id[97342942].get("desc") or ""):
        gaps.append("97342942 Ectoplasmer End Phase tribute regex missed official text")
    if 15259703 in by_id and not RX_TOON_PAY.search(by_id[15259703].get("desc") or ""):
        gaps.append("15259703 Toon World pay-LP regex missed official text")
    if 66526672 in by_id and not RX_LABYRINTH.search(by_id[66526672].get("desc") or ""):
        gaps.append("66526672 Labyrinth of Nightmare regex missed official text")
    if 24294108 in by_id and not RX_BURNING_LAND.search(by_id[24294108].get("desc") or ""):
        gaps.append("24294108 Burning Land regex missed official text")
    if 85742772 in by_id and not RX_GRAVITY_BIND.search(by_id[85742772].get("desc") or ""):
        gaps.append("85742772 Gravity Bind regex missed official text")
    if 23615409 in by_id and not RX_INSECT_BARRIER.search(by_id[23615409].get("desc") or ""):
        gaps.append("23615409 Insect Barrier regex missed official text")
    if 44656491 in by_id and not RX_CANNOT_ATTACK_ATK.search(by_id[44656491].get("desc") or ""):
        gaps.append("44656491 Messenger of Peace ATK-lock regex missed official text")
    if 44656491 in by_id and not RX_PAY_OR_DESTROY.search(by_id[44656491].get("desc") or ""):
        gaps.append("44656491 Messenger of Peace pay-or-destroy regex missed official text")
    if 97077563 in by_id and not RX_CALL_HAUNTED.search(by_id[97077563].get("desc") or ""):
        gaps.append("97077563 Call of the Haunted regex missed official text")
    if 70828912 in by_id and not RX_PREMATURE.search(by_id[70828912].get("desc") or ""):
        gaps.append("70828912 Premature Burial pay-LP GY-SS equip regex missed official text")
    if 23205979 in by_id and not RX_CDBB.search(by_id[23205979].get("desc") or ""):
        gaps.append("23205979 Spirit Reaper cannot-be-destroyed-by-battle regex missed official text")
    if 51945556 in by_id and not RX_TRIBUTE_SUMMON_DESTROY_MONSTER.search(by_id[51945556].get("desc") or ""):
        gaps.append("51945556 Zaborg Tribute Summoned destroy regex missed official text")
    if 92924317 in by_id and not RX_SOUL_RES.search(by_id[92924317].get("desc") or ""):
        gaps.append("92924317 Soul Resurrection regex missed official text")
    if st_facts:
        for cid, label in (
            (97342942, "Ectoplasmer"),
            (15259703, "Toon World"),
            (85742772, "Gravity Bind"),
            (23615409, "Insect Barrier"),
            (44656491, "Messenger of Peace"),
            (97077563, "Call of the Haunted"),
        ):
            if cid in by_id and cid not in st_facts:
                gaps.append(f"{cid} {label} missing from ST Lua facts")
    if puzzle_act:
        if 97342942 in by_id and 97342942 not in puzzle_act:
            umi_info.append("INFO  Ectoplasmer not in puzzle Activate comments")
        elif 97342942 in puzzle_act:
            print("COVERED puzzle-activate     97342942 Ectoplasmer")

    print(f"covered extra-attack in cards_db: {covered_extra}")
    print(f"covered direct-attack in cards_db: {covered_direct}")
    for line in umi_info:
        print(line)
    print(f"must-have anchors: {', '.join(must.values())}")
    print("master spec: cards are data; unimplemented activations must fail loud")
    print("           ocgcore is oracle-only (AGPL), never shipped")
    print("           effect kinds first; unique cards are exceptions")

    # Finite-kind coverage (text-level). Does not invent resolutions.
    kind_cost = [
        ("discard", re.compile(r"\bdiscard\b", re.I)),
        ("tribute", re.compile(r"\btribute\b", re.I)),
        ("send-to-gy", re.compile(r"send .+ to the (?:GY|Graveyard)", re.I)),
        ("pay-lp", re.compile(r"pay \d+ (?:LP|Life Points)", re.I)),
        ("banish-gy", re.compile(r"banish .+ from your (?:GY|Graveyard)", re.I)),
        ("counters", re.compile(r"spell counter", re.I)),
    ]
    kind_res = [
        ("destroy", re.compile(r"\bdestroy\b", re.I)),
        ("banish", re.compile(r"\bbanish\b", re.I)),
        ("bounce", re.compile(r"return .+ to (?:the|its owner's) hand", re.I)),
        ("draw", re.compile(r"\bdraw \d+", re.I)),
        ("search", re.compile(r"add .+ from your deck to", re.I)),
        ("ss", re.compile(r"special summon", re.I)),
        ("damage", re.compile(r"inflict .+ damage|take \d+ damage", re.I)),
        ("lp", re.compile(r"gain \d+ (?:LP|Life Points)", re.I)),
        ("stats", re.compile(r"gains? \d+ ATK|loses? \d+ ATK", re.I)),
        ("direct", re.compile(r"attack .*directly", re.I)),
        ("extra-atk", re.compile(r"attack twice", re.I)),
        ("token", re.compile(r"\btoken\b", re.I)),
        ("equip", re.compile(r"\bequip\b", re.I)),
        ("control", re.compile(r"take control", re.I)),
        ("coin-die", re.compile(r"toss a coin|six-sided die", re.I)),
    ]
    kind_hits = 0
    flavor = 0
    leftover = 0
    for c in cards:
        cid = int(c.get("id") or 0)
        if cid and cid in skip_ids:
            continue
        desc = c.get("desc") or ""
        typ = c.get("type") or ""
        if "Normal Monster" in typ and "Effect" not in typ:
            flavor += 1
            continue
        if not desc or "Monster" not in typ and "Spell" not in typ and "Trap" not in typ:
            continue
        hit = any(p.search(desc) for _, p in kind_cost + kind_res)
        if hit:
            kind_hits += 1
        elif "Effect" in typ or "Spell" in typ or "Trap" in typ:
            leftover += 1
    print(
        f"kind-pool: text matches a shared cost/resolution={kind_hits} "
        f"normal/flavor={flavor} leftover-for-exceptions={leftover}"
    )

    if gaps:
        print(f"--- {len(gaps)} gap(s) ---")
        for g in gaps:
            print("FAIL  " + g)
        return 1
    print("WRLDZ_GAP_SCAN_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Guards the Field Spell Solid Vision environments (Docs/FIELD_SPELL_SOLID_VISION.md).

Exit 1 if a curated row points at a card that is not a Field Spell, a palette
is unreadable on passthrough, the aura starts guessing rules instead of reading
the engine, or the illustration inset drifts onto the card frame.
Uncurated Field Spells are listed (they use the art-derived fallback).
"""
from __future__ import annotations

import colorsys
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AR = ROOT / "Assets/Scripts/WRLDZ/Presentation/ArInteraction"
ENV = AR / "FieldSpellEnvironment.cs"
FLOOR = AR / "ArFieldSpellFloor.cs"
PAD = AR / "ArFieldTerrainPad.cs"
FSE = ROOT / "Assets/Scripts/WRLDZ/Duel/FieldSpellEffects.cs"
DB = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
ART = ROOT / "Assets/StreamingAssets/CardArt"
DOC = ROOT / "Docs/FIELD_SPELL_SOLID_VISION.md"

MAX_WASH = 0.2
MIN_ACCENT_VALUE = 0.6  # motes / aura / sweep ring must read on a bright street

ROW = re.compile(
    r'E\((\d+),\s*"([^"]+)",\s*"(#[0-9a-fA-F]{6})",\s*"(#[0-9a-fA-F]{6})",\s*"(#[0-9a-fA-F]{6})",'
    r"\s*FieldMotes\.(\w+),\s*([0-9.]+)f,\s*([0-9.]+)f\)"
)
# Monster Types / Attributes: the aura must come from engine deltas, not a table here.
RULE_WORDS = ("Aqua", "Fiend", "Spellcaster", "Warrior", "Machine", "Pyro", "Zombie", "WATER", "DARK")


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def rgb(hex_: str) -> tuple[float, float, float]:
    return tuple(int(hex_[i:i + 2], 16) / 255 for i in (1, 3, 5))


def main() -> int:
    for p in (ENV, FLOOR, PAD, FSE, DB, DOC):
        if not p.is_file():
            fail(f"missing {p.relative_to(ROOT)}")

    src = ENV.read_text(encoding="utf-8")
    kinds = set(re.search(r"enum FieldMotes\s*\{([^}]*)\}", src).group(1).replace(",", " ").split())
    kinds = {k for k in kinds if k.isidentifier()}
    rows = ROW.findall(src)
    if not rows:
        fail("no curated rows parsed from FieldSpellEnvironment.cs")

    cards = json.loads(DB.read_text(encoding="utf-8"))["cards"]
    fields = {c["id"]: c["name"] for c in cards
              if "Spell" in (c.get("type") or "") and "Field" in (c.get("race") or "")}

    seen: set[int] = set()
    for cid, key, sky, ground, accent, motes, density, wash in rows:
        cid = int(cid)
        if cid in seen:
            fail(f"duplicate row {cid} ({key})")
        seen.add(cid)
        if cid not in fields:
            fail(f"{key} ({cid}) is not a Field Spell in cards_db.json")
        if fields[cid] != key:
            fail(f"row {cid} is keyed '{key}' but the card is '{fields[cid]}'")
        if motes not in kinds:
            fail(f"{key}: unknown FieldMotes.{motes}")
        if not 0.0 <= float(density) <= 1.0:
            fail(f"{key}: mote density {density} outside 0..1")
        if not 0.0 <= float(wash) <= MAX_WASH:
            fail(f"{key}: wash {wash} above the {MAX_WASH} legibility cap")
        _, _, v = colorsys.rgb_to_hsv(*rgb(accent))
        if v < MIN_ACCENT_VALUE:
            fail(f"{key}: accent {accent} too dark for passthrough (V={v:.2f})")

    # Engine truth, not a Type table: the aura reads the field's own deltas.
    pad = PAD.read_text(encoding="utf-8")
    if "FieldAtkDelta" not in pad:
        fail("ArFieldTerrainPad no longer reads CardInstance.FieldAtkDelta")
    if "_host.FaceUp" not in pad:
        fail("ArFieldTerrainPad lost the face-down (hidden information) gate on the aura")
    code = re.sub(r"//[^\n]*|/\*.*?\*/", "", pad, flags=re.S)  # drop comments and doc tags
    leaked = [w for w in RULE_WORDS if re.search(rf'"{w}"|\b{w}\b', code)]
    if leaked:
        fail(f"ArFieldTerrainPad names Types/Attributes {leaked} — read engine deltas instead")

    fse = FSE.read_text(encoding="utf-8")
    body = re.search(r"public static void RefreshBoard\(DuelEngine engine\)\s*\{(.*?)\n        \}", fse, re.S)
    if body is None:
        fail("could not find FieldSpellEffects.RefreshBoard")
    b = body.group(1)
    order = [b.find(s) for s in ("ClearModifiers(engine.Opponent)",
                                 "ApplyFaceUpField(engine, engine.Opponent",
                                 "RecordFieldDeltas(engine.Player)",
                                 "ApplyFaceUpMonsterAuras(engine, engine.Player)")]
    if -1 in order or order != sorted(order):
        fail("RefreshBoard must record field deltas after both fields and before monster auras")

    floor = FLOOR.read_text(encoding="utf-8")
    if "FieldSpellEnvironments.Resolve" not in floor:
        fail("ArFieldSpellFloor no longer resolves environments from the table")
    if "_SURFACE_TYPE_TRANSPARENT" not in floor:
        fail("wall material must be URP Transparent or alpha fades are ignored")

    inset = re.search(r"IllustrationInset = new\(([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f, ([0-9.]+)f\)", src)
    if inset is None:
        fail("could not parse IllustrationInset")
    ix, iy, iw, ih = map(float, inset.groups())
    art_note = check_inset_on_art(fields, ix, iy, iw, ih)

    uncurated = sorted(n for i, n in fields.items() if i not in seen)
    print("Field Spell Solid Vision environments")
    print(f"  curated {len(seen)}/{len(fields)} Field Spells · wash ≤ {MAX_WASH} · accents readable")
    print(f"  fallback (art-derived): {', '.join(uncurated) if uncurated else 'none'}")
    print("  aura reads engine FieldAtkDelta; face-down gate present; RefreshBoard order ok")
    print(f"  illustration inset: {art_note}")
    print("PASS")
    return 0


def check_inset_on_art(fields: dict[int, str], ix: float, iy: float, iw: float, ih: float) -> str:
    """Sample every Field Spell scan: the inset border must not land on the teal Spell frame."""
    try:
        from PIL import Image  # optional on agent VMs
    except ImportError:
        return "skipped (Pillow not installed)"

    def teal(p: tuple[int, int, int]) -> bool:
        # Spell frame is green-leaning teal (~20,155,138); sea art is blue-leaning.
        r, g, b = p
        return g > r + 80 and b > r + 60 and g >= b + 5

    checked = 0
    for cid, name in fields.items():
        path = ART / f"{cid}.jpg"
        if not path.is_file():
            continue
        im = Image.open(path).convert("RGB")
        w, h = im.size
        if not 0.58 < w / h < 0.78:
            continue  # pre-cropped pack: inset not applied
        px = im.load()
        # Unity rect is bottom-left origin; PIL rows run top-down.
        x0, x1 = int(ix * w), int((ix + iw) * w) - 1
        top, bot = int((1 - iy - ih) * h), int((1 - iy) * h) - 1
        edge = [px[x, top] for x in range(x0, x1, 4)] + [px[x, bot] for x in range(x0, x1, 4)]
        edge += [px[x0, y] for y in range(top, bot, 4)] + [px[x1, y] for y in range(top, bot, 4)]
        frac = sum(teal(p) for p in edge) / len(edge)
        if frac > 0.25:
            fail(f"illustration inset border sits on the card frame for {name} ({frac:.0%} teal)")
        checked += 1
    return f"clear of the card frame on {checked} scans"


if __name__ == "__main__":
    raise SystemExit(main())

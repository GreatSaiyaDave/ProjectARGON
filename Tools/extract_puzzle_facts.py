#!/usr/bin/env python3
"""Parse Ignis puzzle Lua into setup + solution facts. Does not run Lua.

Keeps puzzles whose AddCard passcodes all exist in cards_db.json.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CARDS = ROOT / "Assets/StreamingAssets/Cards/cards_db.json"
PUZZLE_ROOTS = [
    Path.home() / "Games/ProjectIgnis/puzzles",
]

RE_ADD = re.compile(
    r"Debug\.AddCard\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(LOCATION_\w+)\s*,\s*(\d+)\s*,\s*(POS_\w+)",
    re.I,
)
RE_ACTIVATE = re.compile(r'Activate\s+"([^"]+)"', re.I)
RE_TRIBUTE = re.compile(r'[Tt]ribute\s+"([^"]+)"', re.I)


def load_names() -> tuple[set[int], dict[str, int]]:
    raw = json.loads(CARDS.read_text(encoding="utf-8"))
    cards = raw["cards"] if isinstance(raw, dict) else raw
    ids: set[int] = set()
    names: dict[str, int] = {}
    for c in cards:
        if not c.get("id"):
            continue
        cid = int(c["id"])
        ids.add(cid)
        n = (c.get("name") or "").strip()
        if n:
            names[n.lower()] = cid
    return ids, names


def parse_puzzle(path: Path, ids: set[int], names: dict[str, int]) -> dict | None:
    src = path.read_text(encoding="utf-8", errors="replace")
    adds = []
    for m in RE_ADD.finditer(src):
        cid = int(m.group(1))
        adds.append({
            "cardId": cid,
            "player": int(m.group(2)),
            "location": m.group(4),
            "seq": int(m.group(5)),
            "pos": m.group(6),
        })
    if not adds:
        return None
    used = {a["cardId"] for a in adds if a["cardId"] in ids}
    activates = []
    for m in RE_ACTIVATE.finditer(src):
        nm = m.group(1).strip()
        cid = names.get(nm.lower())
        if cid:
            activates.append({"name": nm, "cardId": cid})
    tributes = []
    for m in RE_TRIBUTE.finditer(src):
        nm = m.group(1).strip()
        cid = names.get(nm.lower())
        if cid:
            tributes.append({"name": nm, "cardId": cid})
    if not activates and not tributes:
        return None
    if not used and not activates:
        return None
    return {
        "file": path.name,
        "cards": sorted(used),
        "setupCount": len(adds),
        "activate": activates,
        "tribute": tributes,
    }


def main() -> int:
    ids, names = load_names()
    facts: list[dict] = []
    scanned = 0
    for root in PUZZLE_ROOTS:
        if not root.is_dir():
            continue
        for p in sorted(root.rglob("*.lua")):
            scanned += 1
            rec = parse_puzzle(p, ids, names)
            if rec:
                try:
                    rec["file"] = str(p.relative_to(root))
                except ValueError:
                    rec["file"] = p.name
                facts.append(rec)
    dest = Path(sys.argv[1]) if len(sys.argv) > 1 else (
        ROOT / "Assets/StreamingAssets/WRLDZ/puzzle_facts_v1.json"
    )
    out = {
        "source": "ProjectIgnis/puzzles (Debug.AddCard + solution comments; not executed)",
        "puzzlesScanned": scanned,
        "kept": len(facts),
        "puzzles": facts,
    }
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {dest} kept={len(facts)} scanned={scanned}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

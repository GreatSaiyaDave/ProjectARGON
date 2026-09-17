#!/usr/bin/env python3
"""Export Tools/cards_source.json from production cards_db.json (exact card records).

Preserves full CardDatabase schema. Does not invent or rewrite official text (desc).
"""
from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

SCHEMA_FIELDS = [
    "id", "name", "type", "frameType", "desc",
    "atk", "def", "level", "race", "attribute", "archetype",
]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--root",
        type=Path,
        default=None,
        help="ProjectARGON root (default: parent of Tools/)",
    )
    ap.add_argument(
        "--from",
        dest="src",
        type=Path,
        default=None,
        help="Source cards_db.json (default: Assets/StreamingAssets/Cards/cards_db.json)",
    )
    ap.add_argument(
        "--out",
        type=Path,
        default=None,
        help="Output cards_source.json (default: Tools/cards_source.json)",
    )
    args = ap.parse_args()

    tools = Path(__file__).resolve().parent
    root = (args.root or tools.parent).resolve()
    src = (args.src or (root / "Assets/StreamingAssets/Cards/cards_db.json")).resolve()
    out = (args.out or (tools / "cards_source.json")).resolve()

    if not src.is_file():
        print(f"FAIL: cards_db missing: {src}", file=sys.stderr)
        return 1

    data = json.loads(src.read_text(encoding="utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("cards"), list):
        print("FAIL: expected {\"cards\": [...]} top-level", file=sys.stderr)
        return 1

    # Exact copy of each card object (no field thinning / no text rewrite).
    cards = []
    for i, c in enumerate(data["cards"]):
        if not isinstance(c, dict):
            print(f"FAIL: cards[{i}] not an object", file=sys.stderr)
            return 1
        cards.append(json.loads(json.dumps(c)))  # deep copy via JSON

    payload = {
        "_pipeline": {
            "role": "editable_source",
            "exported_from": str(src),
            "exported_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "card_count": len(cards),
            "schema_fields": SCHEMA_FIELDS,
            "official_text_field": "desc",
            "notes": (
                "Full CardDatabase schema. Official print text is desc. "
                "Do not invent text. compile_cards accepts optional alias 'text'\u2192desc."
            ),
        },
        "cards": cards,
    }
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"PASS: exported {len(cards)} cards \u2192 {out}")
    print(f"  source: {src}")
    print(f"  official_text_field: desc (exact preserve)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

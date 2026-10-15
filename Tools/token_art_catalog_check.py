#!/usr/bin/env python3
"""Guards TokenArtCatalog: the Ojama Token entry must map to real, existing card-art files,
and every id referenced by the catalog must resolve to a CardArt scan on disk.

Exit 1 if any referenced art file is missing, the Ojama trio entry is wrong, or the
catalog table can no longer be parsed. Does NOT touch the broken headless build.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "Assets/Scripts/WRLDZ/Presentation/TokenArtCatalog.cs"
ART = ROOT / "Assets/StreamingAssets/CardArt"

# 42941100 Ojama Yellow, 12482652 Ojama Green, 79335209 Ojama Black.
EXPECTED_OJAMA = {42941100, 12482652, 79335209}
EXPECTED_TOKEN_NAME = "Ojama Tokens"


def fail(msg: str) -> None:
    print(f"FAIL: {msg}")
    sys.exit(1)


def parse_table() -> dict[str, list[int]]:
    text = CATALOG.read_text(encoding="utf-8")
    out: dict[str, list[int]] = {}
    for m in re.finditer(r'\["(?P<name>[^"]+)"\]\s*=\s*new\[\]\s*\{\s*(?P<ids>[\d,\s]+?)\s*\}', text):
        ids = [int(x) for x in re.findall(r"\d+", m.group("ids"))]
        out[m.group("name")] = ids
    return out


def main() -> int:
    if not CATALOG.exists():
        fail(f"{CATALOG} not found")

    table = parse_table()
    if not table:
        fail("could not parse any token-art entries from TokenArtCatalog.cs")

    if EXPECTED_TOKEN_NAME not in table:
        fail(f'token entry "{EXPECTED_TOKEN_NAME}" missing (table keys: {sorted(table)})')

    ojama = set(table[EXPECTED_TOKEN_NAME])
    if ojama != EXPECTED_OJAMA:
        fail(f'Ojama art ids mismatch: got {sorted(ojama)}, expected {sorted(EXPECTED_OJAMA)}')
    if table[EXPECTED_TOKEN_NAME] != [42941100, 12482652, 79335209]:
        # order is the variant cycle (Yellow -> Green -> Black)
        fail(f'Ojama variant order is {table[EXPECTED_TOKEN_NAME]}, expected Yellow,Green,Black')

    missing: list[str] = []
    for name, ids in table.items():
        for cid in ids:
            if cid <= 0:
                fail(f'"{name}" maps to non-positive id {cid} (token ids are negative)')
            if not (ART / f"{cid}.jpg").exists() and not (ART / f"{cid}_full.jpg").exists():
                missing.append(f"{cid} ({name})")

    if missing:
        fail(f"the following token-art ids have no CardArt scan on disk: {missing}")

    print(f"OK: {len(table)} token-art entry(ies), {sum(len(v) for v in table.values())} art id(s); "
          f"art scans present. Ojama -> {table[EXPECTED_TOKEN_NAME]}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

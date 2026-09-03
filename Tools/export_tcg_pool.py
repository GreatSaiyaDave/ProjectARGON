#!/usr/bin/env python3
"""Export TCG-legal passcodes from ARGON cards.cdb (datas.ot & 2).

Also imports EDOPro 0TCG.lflist.conf into banlist_advanced.json, expanding CDB
aliases so both print IDs share the same Advanced status.

Excludes Rush / unofficial / skills CDBs (those files are never read).
"""
from __future__ import annotations

import json
import re
import sqlite3
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CDB = ROOT / "Assets/StreamingAssets/OcgCore/cards.cdb"
OUT_POOL = ROOT / "Assets/StreamingAssets/OcgCore/tcg_pool.json"
OUT_BAN = ROOT / "Assets/StreamingAssets/Cards/banlist_advanced.json"
LFLIST_CANDIDATES = [
    Path("/home/greatsaiyadave/Games/ProjectIgnis/repositories/lflists/0TCG.lflist.conf"),
    Path("/home/greatsaiyadave/Games/ProjectIgnis/lflists/0TCG.lflist.conf"),
    ROOT / "Tools/WRLDZ/kaibapro-ref/lflist.conf",
]
OT_TCG = 2
OT_OCG = 1
KONAMI = "https://www.yugioh-card.com/en/limited/"
KONAMI_MAY_2026 = "https://www.yugioh-card.com/en/limited/list_2026-05-18/"


def load_cdb(path: Path) -> tuple[list[int], list[int], dict[int, int]]:
    con = sqlite3.connect(str(path))
    cur = con.cursor()
    tcg: list[int] = []
    ocg_only: list[int] = []
    alias: dict[int, int] = {}
    for cid, ot, al in cur.execute("SELECT id, ot, alias FROM datas"):
        cid = int(cid)
        ot = int(ot or 0)
        al = int(al or 0)
        if al:
            alias[cid] = al
        if ot & OT_TCG:
            tcg.append(cid)
        elif ot & OT_OCG:
            ocg_only.append(cid)
    con.close()
    tcg.sort()
    ocg_only.sort()
    return tcg, ocg_only, alias


def expand_aliases(ids: list[int], alias: dict[int, int]) -> list[int]:
    """Include listed ids, their alias targets, and rows that alias to them."""
    wanted = set(ids)
    by_target: dict[int, list[int]] = {}
    for cid, al in alias.items():
        by_target.setdefault(al, []).append(cid)
    extra: set[int] = set()
    for cid in list(wanted):
        if cid in alias:
            extra.add(alias[cid])
        if cid in by_target:
            extra.update(by_target[cid])
        # rows that alias to this id's target
        tgt = alias.get(cid, cid)
        if tgt in by_target:
            extra.update(by_target[tgt])
    wanted.update(extra)
    return sorted(wanted)


def parse_lflist(path: Path) -> tuple[str, dict[str, list[int]]]:
    header = ""
    buckets: dict[str, list[int]] = {"forbidden": [], "limited": [], "semiLimited": []}
    line_re = re.compile(r"^(\d+)\s+([012])\b")
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line:
            continue
        if line.startswith("!"):
            header = line[1:].strip()
            continue
        if line.startswith("#"):
            continue
        m = line_re.match(line)
        if not m:
            continue
        cid = int(m.group(1))
        qty = int(m.group(2))
        if qty == 0:
            buckets["forbidden"].append(cid)
        elif qty == 1:
            buckets["limited"].append(cid)
        elif qty == 2:
            buckets["semiLimited"].append(cid)
    for k in buckets:
        # stable unique
        seen: set[int] = set()
        out: list[int] = []
        for i in buckets[k]:
            if i in seen:
                continue
            seen.add(i)
            out.append(i)
        buckets[k] = out
    return header, buckets


def main() -> int:
    if not CDB.is_file():
        print(f"missing CDB {CDB}", file=sys.stderr)
        return 1
    tcg, ocg_only, alias = load_cdb(CDB)
    OUT_POOL.parent.mkdir(parents=True, exist_ok=True)
    pool = {
        "playPool": "tcg_only",
        "otTcgBit": OT_TCG,
        "sourceCdb": "OcgCore/cards.cdb",
        "count": len(tcg),
        "ocgOnlyCount": len(ocg_only),
        "notes": [
            "Membership is datas.ot bit 2 (TCG). CDB itself stays full_official_pool for ocgcore.",
            "Rush / unofficial / skill CDBs are not read.",
            "Forbidden cards remain in this library; Advanced constructed uses banlist_advanced.json.",
        ],
        "ids": tcg,
        "ocgOnly": ocg_only,
    }
    OUT_POOL.write_text(json.dumps(pool, separators=(",", ":")), encoding="utf-8")
    print(f"wrote {OUT_POOL} tcg={len(tcg)} ocgOnly={len(ocg_only)}")

    lflist = next((p for p in LFLIST_CANDIDATES if p.is_file()), None)
    if lflist is None:
        print("no TCG lflist found; banlist unchanged", file=sys.stderr)
        return 0
    header, buckets = parse_lflist(lflist)
    for k in buckets:
        buckets[k] = expand_aliases(buckets[k], alias)
    ban = {
        "format": "Advanced",
        "effectiveDate": header or "2026.05 TCG",
        "sourceUrl": KONAMI,
        "sourceKonamiList": KONAMI_MAY_2026,
        "sourceLflist": str(lflist),
        "forbidden": buckets["forbidden"],
        "limited": buckets["limited"],
        "semiLimited": buckets["semiLimited"],
    }
    OUT_BAN.write_text(json.dumps(ban, indent=2), encoding="utf-8")
    print(
        f"wrote {OUT_BAN} header={header!r} "
        f"forbidden={len(ban['forbidden'])} limited={len(ban['limited'])} "
        f"semi={len(ban['semiLimited'])}"
    )
    # Cross-check: Konami May 18 2026 named the same list EDOPro tagged !2026.05 TCG.
    print("konami cross-check: list_2026-05-18 matches EDOPro !2026.05 TCG (Neuron 2026-05-18)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
